import { randomUUID } from 'node:crypto';
import { Result, makeError } from '../../domain/shared/result.js';
import { MeetingEventType } from '../../domain/meetings/meeting-event-log.js';
import type { MeetingRepository } from '../ports/meeting-repository.js';
import type { IntegrationEventPublisher } from '../ports/integration-event-publisher.js';
import type { SocketSessionTerminator } from '../ports/socket-session-terminator.js';
import { MeetingEventTypes, type MeetingInvitationRevokedEvent } from '../../contracts/events/meeting-events.js';
import { forceLeaveMeeting, type ForceLeaveMeetingDeps } from './force-leave-meeting.js';
import { recordMeetingEvent, type MeetingEventLogDeps } from './meeting-event-log-writer.js';

export interface RevokeMeetingInvitationCommand {
  readonly tenantId: string;
  readonly meetingId: string;
  readonly invitationId: string;
  readonly actorUserId: string;
  readonly correlationId?: string;
}

export interface RevokeMeetingInvitationResult {
  /** F6: el guest de esta invitacion estaba dentro (o esperando) y se le saco. */
  readonly ejected: boolean;
}

/**
 * Lo minimo es el repositorio; el resto (expulsion del guest, evento, log) es opcional para que los
 * dobles de prueba viejos sigan valiendo. Con `ejection` completo se expulsa al guest en vivo.
 */
export interface RevokeMeetingInvitationDeps extends MeetingEventLogDeps {
  readonly meetings: MeetingRepository;
  readonly publisher?: IntegrationEventPublisher | undefined;
  /** Salida forzada + cierre del socket del guest (INV-09 H-5 / D-04). */
  readonly ejection?: (ForceLeaveMeetingDeps & { readonly sessionTerminator?: SocketSessionTerminator | undefined }) | undefined;
}

/** Host/Cohost. Revocar una invitation ya usada/expirada es un no-op valido (idempotente). */
export async function revokeMeetingInvitation(
  command: RevokeMeetingInvitationCommand,
  deps: RevokeMeetingInvitationDeps,
): Promise<Result<RevokeMeetingInvitationResult>> {
  const meeting = await deps.meetings.findById(command.tenantId, command.meetingId);
  if (!meeting) return Result.fail(makeError('Meeting.NotFound', 'Meeting not found.'));

  const canAct = command.actorUserId === meeting.hostUserId || meeting.isCohost(command.actorUserId);
  if (!canAct) return Result.fail(makeError('Meeting.HostOnly', 'Only host or cohost can revoke invitations.'));

  const invitation = await deps.meetings.findInvitationById(command.tenantId, command.invitationId);
  if (!invitation || invitation.toSnapshot().meetingId !== command.meetingId) {
    return Result.fail(makeError('Meeting.Invitation.NotFound', 'Invitation not found.'));
  }

  const now = new Date();
  const alreadyRevoked = invitation.toSnapshot().revokedAtUtc !== null;
  if (!alreadyRevoked) {
    invitation.revoke(now);
    await deps.meetings.saveInvitation(invitation);
  }
  const snap = invitation.toSnapshot();
  const correlationId = command.correlationId ?? `revoke:${snap.id}`;

  // F6: el guest de esta invitacion sigue en la sala (o en la sala de espera) -> fuera, ya.
  let ejected = false;
  if (snap.inviteeKind === 'External' && deps.ejection) {
    const guestUserId = invitation.guestUserId;
    const participant = meeting.toSnapshot().participants.find((p) => p.userId === guestUserId);
    if (participant && (participant.status === 'Joined' || participant.status === 'Waiting')) {
      const left = await forceLeaveMeeting(
        { tenantId: command.tenantId, meetingId: command.meetingId, userId: guestUserId, reason: 'Revoked', correlationId },
        deps.ejection,
      );
      ejected = left.isSuccess;
      await deps.ejection.sessionTerminator?.disconnectUser(command.tenantId, guestUserId);
    }
  }

  if (alreadyRevoked) return Result.ok({ ejected });

  await recordMeetingEvent(deps, {
    tenantId: command.tenantId,
    meetingId: command.meetingId,
    type: MeetingEventType.InvitationRevoked,
    actorUserId: command.actorUserId,
    targetUserId: snap.inviteeUserId,
    payload: { invitationId: snap.id, inviteeKind: snap.inviteeKind, ejected },
    correlationId,
    at: now,
  });
  if (deps.publisher) {
    const event: MeetingInvitationRevokedEvent = {
      eventId: randomUUID(),
      eventType: MeetingEventTypes.InvitationRevoked,
      tenantId: command.tenantId,
      correlationId,
      occurredOnUtc: now.toISOString(),
      invitationId: snap.id,
      meetingId: command.meetingId,
      meetingTitle: meeting.toSnapshot().title,
      inviteeKind: snap.inviteeKind,
      inviteeUserId: snap.inviteeUserId,
      inviteeEmail: snap.inviteeEmail,
      revokedByUserId: command.actorUserId,
      revokedAtUtc: now.toISOString(),
      ejected,
    };
    await deps.publisher.enqueue(event);
  }
  return Result.ok({ ejected });
}
