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
import { logHostAction } from './host-audit.js';

export interface CancelMeetingCommand {
  readonly tenantId: string;
  readonly correlationId: string;
  readonly meetingId: string;
  readonly hostUserId: string;
  readonly reason?: string;
}

export interface CancelMeetingDeps {
  readonly meetings: MeetingRepository;
  readonly publisher: IntegrationEventPublisher;
  readonly emitter: RealtimeEmitter;
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
  const invitedEmails = invitations
    .map((inv) => inv.toSnapshot())
    .filter((s) => s.usedAtUtc === null && s.revokedAtUtc === null && s.inviteeEmail !== null)
    .map((s) => s.inviteeEmail!);

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
  };
  await deps.publisher.enqueue(event);

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

  logHostAction({
    tenantId: cmd.tenantId,
    meetingId: cmd.meetingId,
    action: 'meeting.cancel',
    actorUserId: cmd.hostUserId,
    correlationId: cmd.correlationId,
    metadata: {
      reason: cmd.reason ?? null,
      participantCount: participantUserIds.length,
      invitedEmailCount: invitedEmails.length,
    },
  });

  return Result.okVoid();
}
