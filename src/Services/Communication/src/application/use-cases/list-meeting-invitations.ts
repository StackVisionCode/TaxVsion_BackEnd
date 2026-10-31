import { Result, makeError } from '../../domain/shared/result.js';
import type { MeetingInviteeKind } from '../../domain/meetings/meeting-invitation.js';
import type { MeetingRepository } from '../ports/meeting-repository.js';
import type { UserDirectoryRepository } from '../ports/user-directory-repository.js';
import { directoryNames } from './get-meeting-summary.js';

export interface ListMeetingInvitationsCommand {
  readonly tenantId: string;
  readonly meetingId: string;
  readonly actorUserId: string;
}

export interface MeetingInvitationListItem {
  readonly id: string;
  readonly inviteeKind: MeetingInviteeKind;
  readonly inviteeEmail: string | null;
  readonly inviteeUserId: string | null;
  readonly inviteeName: string | null;
  readonly expiresAt: string;
  readonly usedAt: string | null;
  readonly revokedAt: string | null;
  /** F6 (D-48): auditoria de usos y cierre por la reunion. */
  readonly firstUsedAt: string | null;
  readonly lastUsedAt: string | null;
  readonly useCount: number;
  readonly closedAt: string | null;
  readonly createdAt: string;
}

export interface ListMeetingInvitationsResult {
  readonly invitations: readonly MeetingInvitationListItem[];
}

export interface ListMeetingInvitationsDeps {
  readonly meetings: MeetingRepository;
  /** Nombre de empleados y clientes invitados por id (la invitación no lo guarda). */
  readonly userDirectory?: Pick<UserDirectoryRepository, 'findByUserIds'>;
}

/** Host/Cohost — la lista nunca incluye el token ni su hash, solo metadata de estado. */
export async function listMeetingInvitations(
  command: ListMeetingInvitationsCommand,
  deps: ListMeetingInvitationsDeps,
): Promise<Result<ListMeetingInvitationsResult>> {
  const meeting = await deps.meetings.findById(command.tenantId, command.meetingId);
  if (!meeting) return Result.fail(makeError('Meeting.NotFound', 'Meeting not found.'));

  const canAct = command.actorUserId === meeting.hostUserId || meeting.isCohost(command.actorUserId);
  if (!canAct) return Result.fail(makeError('Meeting.HostOnly', 'Only host or cohost can list invitations.'));

  const rows = (await deps.meetings.listInvitationsByMeeting(command.tenantId, command.meetingId)).map((i) => i.toSnapshot());
  const names = await directoryNames(
    rows.flatMap((s) => (!s.inviteeName && s.inviteeUserId ? [s.inviteeUserId] : [])),
    deps.userDirectory,
  );
  const invitations = rows.map((s) => {
    return {
      id: s.id,
      inviteeKind: s.inviteeKind,
      inviteeEmail: s.inviteeEmail,
      inviteeUserId: s.inviteeUserId,
      inviteeName: s.inviteeName ?? (s.inviteeUserId ? (names.get(s.inviteeUserId.toLowerCase()) ?? null) : null),
      expiresAt: s.expiresAtUtc.toISOString(),
      usedAt: s.usedAtUtc ? s.usedAtUtc.toISOString() : null,
      revokedAt: s.revokedAtUtc ? s.revokedAtUtc.toISOString() : null,
      firstUsedAt: s.firstUsedAtUtc ? s.firstUsedAtUtc.toISOString() : null,
      lastUsedAt: s.lastUsedAtUtc ? s.lastUsedAtUtc.toISOString() : null,
      useCount: s.useCount,
      closedAt: s.closedAtUtc ? s.closedAtUtc.toISOString() : null,
      createdAt: s.createdAtUtc.toISOString(),
    };
  });

  return Result.ok({ invitations });
}
