import { randomUUID } from 'node:crypto';
import { Result, makeError } from '../../domain/shared/result.js';
import type { MeetingRepository } from '../ports/meeting-repository.js';
import type { IntegrationEventPublisher } from '../ports/integration-event-publisher.js';
import type { RealtimeEmitter } from '../ports/realtime-emitter.js';
import {
  MeetingEventTypes,
  type MeetingParticipantDeniedEvent,
} from '../../contracts/events/meeting-events.js';
import {
  MeetingSocketEvents,
  type MeetingParticipantDeniedDto,
} from '../../contracts/socket/meeting-socket-events.js';
import { logHostAction } from './host-audit.js';

export interface DenyWaitingRoomCommand {
  readonly tenantId: string;
  readonly correlationId: string;
  readonly meetingId: string;
  readonly hostUserId: string;
  readonly targetUserId: string;
}

export interface DenyWaitingRoomDeps {
  readonly meetings: MeetingRepository;
  readonly publisher: IntegrationEventPublisher;
  readonly emitter: RealtimeEmitter;
}

/**
 * Host/Cohost rechaza a alguien de la sala de espera. Publica el integration
 * event (Notification enviara un mail "tu solicitud fue rechazada"), emite un
 * socket dirigido al target para que su UI cierre el spinner, y broadcast al
 * meeting para que el resto vea que la lista de espera se achica.
 */
export async function denyWaitingRoomParticipant(
  cmd: DenyWaitingRoomCommand,
  deps: DenyWaitingRoomDeps,
): Promise<Result<void>> {
  const meeting = await deps.meetings.findById(cmd.tenantId, cmd.meetingId);
  if (!meeting) return Result.fail(makeError('Meeting.NotFound', 'Meeting not found.'));

  const result = meeting.denyWaitingRoom({ hostUserId: cmd.hostUserId, targetUserId: cmd.targetUserId });
  if (!result.isSuccess) return Result.fail(result.error);
  await deps.meetings.save(meeting);

  const now = new Date();
  const event: MeetingParticipantDeniedEvent = {
    eventId: randomUUID(),
    eventType: MeetingEventTypes.ParticipantDenied,
    tenantId: cmd.tenantId,
    correlationId: cmd.correlationId,
    occurredOnUtc: now.toISOString(),
    meetingId: cmd.meetingId,
    participantUserId: cmd.targetUserId,
    deniedByUserId: cmd.hostUserId,
    deniedAtUtc: now.toISOString(),
  };
  await deps.publisher.enqueue(event);

  const dto: MeetingParticipantDeniedDto = {
    meetingId: cmd.meetingId,
    participantUserId: cmd.targetUserId,
    deniedByUserId: cmd.hostUserId,
    deniedAtUtc: now.toISOString(),
  };

  deps.emitter.emitToUser({
    tenantId: cmd.tenantId,
    userId: cmd.targetUserId,
    event: MeetingSocketEvents.ParticipantDenied,
    envelope: {
      eventId: randomUUID(),
      correlationId: cmd.correlationId,
      emittedAtUtc: now.toISOString(),
      payload: dto,
    },
  });
  deps.emitter.emitToMeeting({
    tenantId: cmd.tenantId,
    meetingId: cmd.meetingId,
    event: MeetingSocketEvents.ParticipantDenied,
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
    action: 'meeting.deny_waiting_room',
    actorUserId: cmd.hostUserId,
    targetUserId: cmd.targetUserId,
    correlationId: cmd.correlationId,
  });

  return Result.okVoid();
}
