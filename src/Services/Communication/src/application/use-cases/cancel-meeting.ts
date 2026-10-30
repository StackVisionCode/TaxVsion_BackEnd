import { randomUUID } from 'node:crypto';
import { Result, makeError } from '../../domain/shared/result.js';
import type { MeetingRepository } from '../ports/meeting-repository.js';
import type { IntegrationEventPublisher } from '../ports/integration-event-publisher.js';
import type { RealtimeEmitter } from '../ports/realtime-emitter.js';
import {
  MeetingEventTypes,
  type MeetingCancelledEvent,
} from '../../contracts/events/meeting-events.js';
import { MeetingSocketEvents, type MeetingCancelledDto } from '../../contracts/socket/meeting-socket-events.js';
import { MeetingEventType } from '../../domain/meetings/meeting-event-log.js';
import { recordMeetingEvent, type MeetingEventLogDeps } from './meeting-event-log-writer.js';
import { closeMeetingInvitations } from './close-meeting-invitations.js';
import { resolveUserDisplayName } from './resolve-user-display-name.js';
import type { UserDirectoryRepository } from '../ports/user-directory-repository.js';
import type { TenantHostResolver } from '../ports/tenant-host-resolver.js';

export interface CancelMeetingCommand {
  readonly tenantId: string;
  readonly correlationId: string;
  readonly meetingId: string;
  readonly hostUserId: string;
  readonly reason?: string;
}

export interface CancelMeetingDeps extends MeetingEventLogDeps {
  readonly meetings: MeetingRepository;
  readonly publisher: IntegrationEventPublisher;
  readonly emitter: RealtimeEmitter;
  /** F6: nombre del host y zona horaria para el email y el `.ics`. */
  readonly userDirectory?: Pick<UserDirectoryRepository, 'findByUserId'> | undefined;
  readonly tenantHostResolver?: Pick<TenantHostResolver, 'resolveTimeZone'> | undefined;
}

/**
 * Solo el host puede cancelar (regla del dominio — cohost no). Recolecta
 * participantes YA registrados + emails de invitados-por-link que aun no
 * habian entrado, para que Notification pueda mandarle mail a ambos grupos
 * ("el meeting fue cancelado").
 */
export async function cancelMeeting(
  cmd: CancelMeetingCommand,
  deps: CancelMeetingDeps,
): Promise<Result<void>> {
  const meeting = await deps.meetings.findById(cmd.tenantId, cmd.meetingId);
  if (!meeting) return Result.fail(makeError('Meeting.NotFound', 'Meeting not found.'));

  const result = meeting.cancel({ hostUserId: cmd.hostUserId });
  if (!result.isSuccess) return Result.fail(result.error);
  await deps.meetings.save(meeting);

  const snapshot = meeting.toSnapshot();
  const participantUserIds = snapshot.participants.map((p) => p.userId);
  const invitations = await deps.meetings.listInvitationsByMeeting(cmd.tenantId, cmd.meetingId);
  const invitationSnapshots = invitations.map((inv) => inv.toSnapshot()).filter((s) => s.revokedAtUtc === null);
  // F6: externos sin cuenta por email; los de cuenta reciben in-app (inviteeUserIds).
  const invitedEmails = invitationSnapshots.filter((s) => s.inviteeUserId === null && s.inviteeEmail !== null).map((s) => s.inviteeEmail!);
  const inviteeUserIds = [...new Set(invitationSnapshots.filter((s) => s.inviteeUserId !== null).map((s) => s.inviteeUserId!))];
  const hostDisplayName = await resolveUserDisplayName(
    deps.userDirectory,
    snapshot.hostUserId,
    snapshot.participants.find((p) => p.userId === snapshot.hostUserId)?.displayName,
  );
  const timeZone = (await deps.tenantHostResolver?.resolveTimeZone?.(cmd.tenantId)) ?? null;

  const now = new Date();
  const event: MeetingCancelledEvent = {
    eventId: randomUUID(),
    eventType: MeetingEventTypes.Cancelled,
    tenantId: cmd.tenantId,
    correlationId: cmd.correlationId,
    occurredOnUtc: now.toISOString(),
    meetingId: cmd.meetingId,
    cancelledByUserId: cmd.hostUserId,
    cancelledAtUtc: now.toISOString(),
    participantUserIds,
    invitedEmails,
    reason: cmd.reason ?? null,
    meetingTitle: snapshot.title,
    shortCode: snapshot.shortCode,
    scheduledForUtc: snapshot.scheduledForUtc ? snapshot.scheduledForUtc.toISOString() : null,
    durationMinutes: snapshot.scheduledDurationMinutes ?? snapshot.maxDurationMinutes,
    hostUserId: snapshot.hostUserId,
    hostDisplayName,
    timeZone,
    inviteeUserIds,
  };
  await deps.publisher.enqueue(event);
  // F6 (D-48): nadie entra con una invitacion de una reunion cancelada.
  await closeMeetingInvitations({ tenantId: cmd.tenantId, meetingId: cmd.meetingId, reason: 'Cancelled', correlationId: cmd.correlationId, now }, deps);

  const dto: MeetingCancelledDto = {
    meetingId: cmd.meetingId,
    cancelledByUserId: cmd.hostUserId,
    reason: cmd.reason ?? null,
    cancelledAtUtc: now.toISOString(),
  };
  deps.emitter.emitToMeeting({
    tenantId: cmd.tenantId,
    meetingId: cmd.meetingId,
    event: MeetingSocketEvents.Cancelled,
    envelope: {
      eventId: randomUUID(),
      correlationId: cmd.correlationId,
      emittedAtUtc: now.toISOString(),
      payload: dto,
    },
  });

  await recordMeetingEvent(deps, {
    tenantId: cmd.tenantId,
    meetingId: cmd.meetingId,
    type: MeetingEventType.Cancelled,
    actorUserId: cmd.hostUserId,
    payload: { reason: cmd.reason ?? null, participantCount: participantUserIds.length, invitedEmailCount: invitedEmails.length },
    correlationId: cmd.correlationId,
    at: now,
  });

  return Result.okVoid();
}
