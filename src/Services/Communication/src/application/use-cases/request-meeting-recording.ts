import { randomUUID } from 'node:crypto';
import { Result, makeError } from '../../domain/shared/result.js';
import { RecordingScope } from '../../domain/recording/recording-session-state.js';
import type { MeetingRepository } from '../ports/meeting-repository.js';
import type { RecordingSessionRepository } from '../ports/recording-repository.js';
import type { IdempotencyStore } from '../ports/idempotency-store.js';
import type { IntegrationEventPublisher } from '../ports/integration-event-publisher.js';
import type { RealtimeEmitter } from '../ports/realtime-emitter.js';
import {
  MeetingEventTypes,
  type MeetingRecordingConsentRequestedEvent,
} from '../../contracts/events/meeting-events.js';
import {
  MeetingSocketEvents,
  type MeetingRecordingConsentRequestedDto,
} from '../../contracts/socket/meeting-socket-events.js';

/**
 * Socket trigger: `meeting.recording.start_request` — pese al nombre del
 * evento socket ("start_request"), esto NO arranca la grabacion todavia,
 * solo abre el ciclo de consentimiento (Meeting.requestRecording, Idle ->
 * Requesting). El arranque real (Recording) lo hace start-meeting-recording.ts,
 * disparado automaticamente cuando la policy se satisface.
 */
export interface RequestMeetingRecordingCommand {
  readonly tenantId: string;
  readonly correlationId: string;
  readonly clientKey: string;
  readonly meetingId: string;
  readonly actorUserId: string;
}

export interface RequestMeetingRecordingResult {
  readonly meetingId: string;
  readonly participantUserIds: readonly string[];
  readonly requestedAtUtc: string;
}

export interface RequestMeetingRecordingDeps {
  readonly meetings: MeetingRepository;
  readonly recordingSessions: RecordingSessionRepository;
  readonly idempotency: IdempotencyStore;
  readonly publisher: IntegrationEventPublisher;
  readonly emitter: RealtimeEmitter;
}

export async function requestMeetingRecording(
  command: RequestMeetingRecordingCommand,
  deps: RequestMeetingRecordingDeps,
): Promise<Result<RequestMeetingRecordingResult>> {
  const reservation = await deps.idempotency.tryReserve<RequestMeetingRecordingResult>({
    tenantId: command.tenantId,
    userId: command.actorUserId,
    scope: 'meeting.recording.request',
    clientKey: command.clientKey,
    ttlSeconds: 60,
  });
  if (reservation.status === 'replay') return Result.ok(reservation.payload);

  const release = () =>
    deps.idempotency.release({
      tenantId: command.tenantId,
      userId: command.actorUserId,
      scope: 'meeting.recording.request',
      clientKey: command.clientKey,
      token: reservation.token,
    });

  const meeting = await deps.meetings.findById(command.tenantId, command.meetingId);
  if (!meeting) {
    await release();
    return Result.fail(makeError('Meeting.NotFound', 'Meeting not found.'));
  }

  const existingSession = await deps.recordingSessions.findByScope(
    command.tenantId,
    RecordingScope.Meeting,
    command.meetingId,
  );

  const requestResult = meeting.requestRecording({ actorUserId: command.actorUserId, existingSession });
  if (!requestResult.isSuccess) {
    await release();
    return Result.fail(requestResult.error);
  }
  await deps.recordingSessions.save(requestResult.value.session);

  const requestedAtUtc = requestResult.value.session.toSnapshot().requestedAtUtc.toISOString();

  const event: MeetingRecordingConsentRequestedEvent = {
    eventId: randomUUID(),
    eventType: MeetingEventTypes.RecordingConsentRequested,
    tenantId: command.tenantId,
    correlationId: command.correlationId,
    occurredOnUtc: requestedAtUtc,
    meetingId: command.meetingId,
    requestedByUserId: command.actorUserId,
    participants: requestResult.value.participantUserIds,
    requestedAtUtc,
  };
  await deps.publisher.enqueue(event);

  const dto: MeetingRecordingConsentRequestedDto = {
    meetingId: command.meetingId,
    requestedByUserId: command.actorUserId,
    requestedAtUtc,
  };
  deps.emitter.emitToMeeting({
    tenantId: command.tenantId,
    meetingId: command.meetingId,
    event: MeetingSocketEvents.RecordingConsentRequested,
    envelope: {
      eventId: randomUUID(),
      correlationId: command.correlationId,
      emittedAtUtc: new Date().toISOString(),
      payload: dto,
    },
  });

  const result: RequestMeetingRecordingResult = {
    meetingId: command.meetingId,
    participantUserIds: requestResult.value.participantUserIds,
    requestedAtUtc,
  };
  await deps.idempotency.commit({
    tenantId: command.tenantId,
    userId: command.actorUserId,
    scope: 'meeting.recording.request',
    clientKey: command.clientKey,
    payload: result,
    token: reservation.token,
  });
  return Result.ok(result);
}
