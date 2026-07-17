import { randomUUID } from 'node:crypto';
import { Result, makeError } from '../../domain/shared/result.js';
import { RecordingScope } from '../../domain/recording/recording-session-state.js';
import type { CallRepository } from '../ports/call-repository.js';
import type { RecordingSessionRepository } from '../ports/recording-repository.js';
import type { IdempotencyStore } from '../ports/idempotency-store.js';
import type { IntegrationEventPublisher } from '../ports/integration-event-publisher.js';
import type { RealtimeEmitter } from '../ports/realtime-emitter.js';
import { CallEventTypes, type CallRecordingStoppedEvent } from '../../contracts/events/call-events.js';
import { CallSocketEvents, type CallRecordingStateChangedDto } from '../../contracts/socket/call-socket-events.js';

export interface StopCallRecordingCommand {
  readonly tenantId: string;
  readonly correlationId: string;
  readonly clientKey: string;
  readonly callId: string;
  readonly actorUserId: string;
}

export interface StopCallRecordingResult {
  readonly callId: string;
  readonly elapsedSeconds: number;
}

export interface StopCallRecordingDeps {
  readonly calls: CallRepository;
  readonly recordingSessions: RecordingSessionRepository;
  readonly idempotency: IdempotencyStore;
  readonly publisher: IntegrationEventPublisher;
  readonly emitter: RealtimeEmitter;
}

export async function stopCallRecording(
  command: StopCallRecordingCommand,
  deps: StopCallRecordingDeps,
): Promise<Result<StopCallRecordingResult>> {
  const reservation = await deps.idempotency.tryReserve<StopCallRecordingResult>({
    tenantId: command.tenantId,
    userId: command.actorUserId,
    scope: 'call.recording.stop',
    clientKey: command.clientKey,
    ttlSeconds: 60,
  });
  if (reservation.status === 'replay') return Result.ok(reservation.payload);

  const release = () =>
    deps.idempotency.release({
      tenantId: command.tenantId,
      userId: command.actorUserId,
      scope: 'call.recording.stop',
      clientKey: command.clientKey,
      token: reservation.token,
    });

  const call = await deps.calls.findById(command.tenantId, command.callId);
  if (!call) {
    await release();
    return Result.fail(makeError('Call.NotFound', 'Call not found.'));
  }

  const session = await deps.recordingSessions.findByScope(command.tenantId, RecordingScope.Call, command.callId);
  if (!session) {
    await release();
    return Result.fail(makeError('RecordingSession.NotFound', 'No recording session for this call.'));
  }

  const beforeStop = session.toSnapshot();
  const stopResult = call.stopRecording({ actorUserId: command.actorUserId, session });
  if (!stopResult.isSuccess) {
    await release();
    return Result.fail(stopResult.error);
  }
  await deps.recordingSessions.save(stopResult.value);

  const snap = stopResult.value.toSnapshot();
  const stoppedAtUtc = (snap.stoppedAtUtc ?? new Date()).toISOString();
  const startedAtMs = (beforeStop.startedAtUtc ?? new Date()).getTime();
  const elapsedSeconds = Math.max(0, Math.floor((new Date(stoppedAtUtc).getTime() - startedAtMs) / 1000));

  const event: CallRecordingStoppedEvent = {
    eventId: randomUUID(),
    eventType: CallEventTypes.RecordingStopped,
    tenantId: command.tenantId,
    correlationId: command.correlationId,
    occurredOnUtc: stoppedAtUtc,
    callId: command.callId,
    stoppedByUserId: command.actorUserId,
    stoppedAtUtc,
    elapsedSeconds,
  };
  await deps.publisher.enqueue(event);

  const dto: CallRecordingStateChangedDto = { callId: command.callId, state: 'Stopping', updatedAtUtc: stoppedAtUtc };
  deps.emitter.emitToCall({
    tenantId: command.tenantId,
    callId: command.callId,
    event: CallSocketEvents.RecordingStateChanged,
    envelope: {
      eventId: randomUUID(),
      correlationId: command.correlationId,
      emittedAtUtc: new Date().toISOString(),
      payload: dto,
    },
  });

  const result: StopCallRecordingResult = { callId: command.callId, elapsedSeconds };
  await deps.idempotency.commit({
    tenantId: command.tenantId,
    userId: command.actorUserId,
    scope: 'call.recording.stop',
    clientKey: command.clientKey,
    payload: result,
    token: reservation.token,
  });
  return Result.ok(result);
}
