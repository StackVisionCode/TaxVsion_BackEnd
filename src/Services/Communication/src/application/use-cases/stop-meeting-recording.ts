import { randomUUID } from 'node:crypto';
import { Result, makeError } from '../../domain/shared/result.js';
import { RecordingScope } from '../../domain/recording/recording-session-state.js';
import type { MeetingRepository } from '../ports/meeting-repository.js';
import type { RecordingSessionRepository } from '../ports/recording-repository.js';
import type { IdempotencyStore } from '../ports/idempotency-store.js';
import type { IntegrationEventPublisher } from '../ports/integration-event-publisher.js';
import type { RealtimeEmitter } from '../ports/realtime-emitter.js';
import { MeetingEventTypes, type MeetingRecordingStoppedEvent } from '../../contracts/events/meeting-events.js';
import {
  MeetingSocketEvents,
  type MeetingRecordingStateChangedDto,
} from '../../contracts/socket/meeting-socket-events.js';

export interface StopMeetingRecordingCommand {
  readonly tenantId: string;
  readonly correlationId: string;
  readonly clientKey: string;
  readonly meetingId: string;
  readonly actorUserId: string;
}

export interface StopMeetingRecordingResult {
  readonly meetingId: string;
  readonly elapsedSeconds: number;
}

export interface StopMeetingRecordingDeps {
  readonly meetings: MeetingRepository;
  readonly recordingSessions: RecordingSessionRepository;
  readonly idempotency: IdempotencyStore;
  readonly publisher: IntegrationEventPublisher;
  readonly emitter: RealtimeEmitter;
}

export async function stopMeetingRecording(
  command: StopMeetingRecordingCommand,
  deps: StopMeetingRecordingDeps,
): Promise<Result<StopMeetingRecordingResult>> {
  const reservation = await deps.idempotency.tryReserve<StopMeetingRecordingResult>({
    tenantId: command.tenantId,
    userId: command.actorUserId,
    scope: 'meeting.recording.stop',
    clientKey: command.clientKey,
    ttlSeconds: 60,
  });
  if (reservation.status === 'replay') return Result.ok(reservation.payload);

  const release = () =>
    deps.idempotency.release({
      tenantId: command.tenantId,
      userId: command.actorUserId,
      scope: 'meeting.recording.stop',
      clientKey: command.clientKey,
      token: reservation.token,
    });

  const meeting = await deps.meetings.findById(command.tenantId, command.meetingId);
  if (!meeting) {
    await release();
    return Result.fail(makeError('Meeting.NotFound', 'Meeting not found.'));
  }

  const session = await deps.recordingSessions.findByScope(command.tenantId, RecordingScope.Meeting, command.meetingId);
  if (!session) {
    await release();
    return Result.fail(makeError('RecordingSession.NotFound', 'No recording session for this meeting.'));
  }

  const beforeStop = session.toSnapshot();
  const stopResult = meeting.stopRecording({ actorUserId: command.actorUserId, session });
  if (!stopResult.isSuccess) {
    await release();
    return Result.fail(stopResult.error);
  }
  await deps.recordingSessions.save(stopResult.value);

  const snap = stopResult.value.toSnapshot();
  const stoppedAtUtc = (snap.stoppedAtUtc ?? new Date()).toISOString();
  const startedAtMs = (beforeStop.startedAtUtc ?? new Date()).getTime();
  const elapsedSeconds = Math.max(0, Math.floor((new Date(stoppedAtUtc).getTime() - startedAtMs) / 1000));

  const event: MeetingRecordingStoppedEvent = {
    eventId: randomUUID(),
    eventType: MeetingEventTypes.RecordingStopped,
    tenantId: command.tenantId,
    correlationId: command.correlationId,
    occurredOnUtc: stoppedAtUtc,
    meetingId: command.meetingId,
    stoppedByUserId: command.actorUserId,
    stoppedAtUtc,
    elapsedSeconds,
  };
  await deps.publisher.enqueue(event);

  const dto: MeetingRecordingStateChangedDto = { meetingId: command.meetingId, state: 'Stopping', updatedAtUtc: stoppedAtUtc };
  deps.emitter.emitToMeeting({
    tenantId: command.tenantId,
    meetingId: command.meetingId,
    event: MeetingSocketEvents.RecordingStateChanged,
    envelope: {
      eventId: randomUUID(),
      correlationId: command.correlationId,
      emittedAtUtc: new Date().toISOString(),
      payload: dto,
    },
  });

  const result: StopMeetingRecordingResult = { meetingId: command.meetingId, elapsedSeconds };
  await deps.idempotency.commit({
    tenantId: command.tenantId,
    userId: command.actorUserId,
    scope: 'meeting.recording.stop',
    clientKey: command.clientKey,
    payload: result,
    token: reservation.token,
  });
  return Result.ok(result);
}
