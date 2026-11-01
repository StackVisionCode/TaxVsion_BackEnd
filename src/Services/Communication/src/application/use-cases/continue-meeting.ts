import { Result, makeError } from '../../domain/shared/result.js';
import { isTerminalMeetingStatus } from '../../domain/meetings/meeting-enums.js';
import { scheduleMeeting, type ScheduleMeetingDeps, type ScheduleMeetingResult } from './schedule-meeting.js';
import {
  createMeetingInvitations,
  type CreateMeetingInvitationInput,
  type CreateMeetingInvitationsDeps,
} from './create-meeting-invitations.js';

export interface ContinueMeetingCommand {
  readonly tenantId: string;
  readonly correlationId: string;
  readonly meetingId: string;
  readonly actorUserId: string;
  readonly actorDisplayName: string;
}

export interface ContinueMeetingResult extends ScheduleMeetingResult {
  readonly invitationsCreated: number;
}

export type ContinueMeetingDeps = ScheduleMeetingDeps & CreateMeetingInvitationsDeps;

/**
 * "Start a new meeting" (D-34): no hay prórroga; al terminar por tiempo el host crea una reunión
 * nueva enlazada (`continuationOfMeetingId`) con los mismos invitados y su propio límite. Reusa el
 * agendado y las invitaciones existentes: cada invitado recibe `meeting.invited` y entra con un clic.
 */
export async function continueMeeting(
  cmd: ContinueMeetingCommand,
  deps: ContinueMeetingDeps,
): Promise<Result<ContinueMeetingResult>> {
  const original = await deps.meetings.findById(cmd.tenantId, cmd.meetingId);
  if (!original) return Result.fail(makeError('Meeting.NotFound', 'Meeting not found.'));
  const snapshot = original.toSnapshot();
  if (!isTerminalMeetingStatus(snapshot.status)) {
    return Result.fail(makeError('Meeting.StillActive', 'Only an ended meeting can be continued.'));
  }
  const wasCohost = snapshot.participants.some((p) => p.userId === cmd.actorUserId && p.role === 'Cohost');
  if (cmd.actorUserId !== snapshot.hostUserId && !wasCohost) {
    return Result.fail(makeError('Meeting.HostOnly', 'Only the host or a co-host can continue the meeting.'));
  }

  const scheduled = await scheduleMeeting(
    {
      tenantId: cmd.tenantId,
      correlationId: cmd.correlationId,
      host: { userId: cmd.actorUserId, displayName: cmd.actorDisplayName },
      title: continuationTitle(snapshot.title),
      description: snapshot.description,
      maxParticipants: snapshot.maxParticipants,
      requireWaitingRoom: snapshot.requireWaitingRoom,
      recordingRequested: snapshot.recordingRequested,
      transcriptLanguage: snapshot.transcriptLanguage,
      continuationOfMeetingId: snapshot.id,
    },
    deps,
  );
  if (!scheduled.isSuccess) return Result.fail(scheduled.error);

  const invitees = await collectInvitees(cmd, snapshot, deps);
  let invitationsCreated = 0;
  if (invitees.length > 0) {
    const invited = await createMeetingInvitations(
      {
        tenantId: cmd.tenantId,
        correlationId: cmd.correlationId,
        meetingId: scheduled.value.meetingId,
        actorUserId: cmd.actorUserId,
        invitees,
      },
      deps,
    );
    if (invited.isSuccess) invitationsCreated = invited.value.invitations.length;
  }
  return Result.ok({ ...scheduled.value, invitationsCreated });
}

/** "(cont.)" una sola vez, sin pasar los 200 caracteres del título. */
export function continuationTitle(title: string): string {
  const suffix = ' (cont.)';
  const base = title.endsWith(suffix) ? title.slice(0, -suffix.length) : title;
  return `${base.slice(0, 200 - suffix.length)}${suffix}`;
}

/** Los que participaron (menos quien continúa) más los invitados que nunca entraron. */
async function collectInvitees(
  cmd: ContinueMeetingCommand,
  snapshot: ReturnType<ContinueMeetingDeps['meetings']['findById']> extends Promise<infer M>
    ? NonNullable<M> extends { toSnapshot(): infer S } ? S : never
    : never,
  deps: ContinueMeetingDeps,
): Promise<CreateMeetingInvitationInput[]> {
  const seen = new Set<string>([cmd.actorUserId]);
  const invitees: CreateMeetingInvitationInput[] = [];
  for (const p of snapshot.participants) {
    if (seen.has(p.userId) || p.actorType === 'Guest') continue;
    seen.add(p.userId);
    if (p.customerId) {
      invitees.push({ kind: 'Customer', userId: p.userId, customerId: p.customerId, name: p.displayName });
    } else {
      invitees.push({ kind: 'Employee', userId: p.userId, name: p.displayName });
    }
  }
  const invitations = await deps.meetings.listInvitationsByMeeting(cmd.tenantId, snapshot.id);
  for (const inv of invitations.map((i) => i.toSnapshot())) {
    if (inv.revokedAtUtc !== null) continue;
    if (inv.inviteeUserId) {
      if (seen.has(inv.inviteeUserId)) continue;
      seen.add(inv.inviteeUserId);
      invitees.push({
        kind: inv.inviteeKind === 'Customer' ? 'Customer' : 'Employee',
        userId: inv.inviteeUserId,
        ...(inv.inviteeEmail ? { email: inv.inviteeEmail } : {}),
        ...(inv.inviteeName ? { name: inv.inviteeName } : {}),
      });
    } else if (inv.inviteeEmail) {
      const key = `email:${inv.inviteeEmail.toLowerCase()}`;
      if (seen.has(key)) continue;
      seen.add(key);
      invitees.push({ kind: 'External', email: inv.inviteeEmail, ...(inv.inviteeName ? { name: inv.inviteeName } : {}) });
    }
  }
  return invitees;
}
