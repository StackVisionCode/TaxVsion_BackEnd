import { randomUUID } from 'node:crypto';
import { Result, makeError } from '../../domain/shared/result.js';
import { RecordingScope } from '../../domain/recording/recording-session-state.js';
import type { CallRepository } from '../ports/call-repository.js';
import type { RecordingSessionRepository } from '../ports/recording-repository.js';
import type { IdempotencyStore } from '../ports/idempotency-store.js';
import type { IntegrationEventPublisher } from '../ports/integration-event-publisher.js';
import type { RealtimeEmitter } from '../ports/realtime-emitter.js';
import { CallEventTypes, type CallRecordingConsentRequestedEvent } from '../../contracts/events/call-events.js';
import {
  CallSocketEvents,
  type CallRecordingConsentRequestedDto,
} from '../../contracts/socket/call-socket-events.js';

/**
 * Socket trigger: `call.recording.start_request` — igual que en meetings, el
 * nombre del evento no arranca la grabacion todavia, solo abre el ciclo de
 * consentimiento (Call.requestRecording, Idle -> Requesting). Cualquiera de
 * las 2 partes puede pedirlo (caller o callee) — Call.requestRecording ya
 * valida isParticipant.
 */
export interface RequestCallRecordingCommand {
  readonly tenantId: string;
  readonly correlationId: string;
  readonly clientKey: string;
  readonly callId: string;
  readonly actorUserId: string;
}

export interface RequestCallRecordingResult {
  readonly callId: string;
  readonly participantUserIds: readonly string[];
  readonly requestedAtUtc: string;
}

export interface RequestCallRecordingDeps {
  readonly calls: CallRepository;
  readonly recordingSessions: RecordingSessionRepository;
  readonly idempotency: IdempotencyStore;
  readonly publisher: IntegrationEventPublisher;
  readonly emitter: RealtimeEmitter;
}

export async function requestCallRecording(
  command: RequestCallRecordingCommand,
  deps: RequestCallRecordingDeps,
): Promise<Result<RequestCallRecordingResult>> {
  const reservation = await deps.idempotency.tryReserve<RequestCallRecordingResult>({
    tenantId: command.tenantId,
    userId: command.actorUserId,
    scope: 'call.recording.request',
    clientKey: command.clientKey,
    ttlSeconds: 60,
  });
  if (reservation.status === 'replay') return Result.ok(reservation.payload);

  const release = () =>
    deps.idempotency.release({
      tenantId: command.tenantId,
      userId: command.actorUserId,
      scope: 'call.recording.request',
      clientKey: command.clientKey,
      token: reservation.token,
    });

  const call = await deps.calls.findById(command.tenantId, command.callId);
  if (!call) {
    await release();
    return Result.fail(makeError('Call.NotFound', 'Call not found.'));
  }

  const existingSession = await deps.recordingSessions.findByScope(command.tenantId, RecordingScope.Call, command.callId);

  const requestResult = call.requestRecording({ actorUserId: command.actorUserId, existingSession });
  if (!requestResult.isSuccess) {
    await release();
    return Result.fail(requestResult.error);
  }
  await deps.recordingSessions.save(requestResult.value.session);

  const requestedAtUtc = requestResult.value.session.toSnapshot().requestedAtUtc.toISOString();

  const event: CallRecordingConsentRequestedEvent = {
    eventId: randomUUID(),
    eventType: CallEventTypes.RecordingConsentRequested,
    tenantId: command.tenantId,
    correlationId: command.correlationId,
    occurredOnUtc: requestedAtUtc,
    callId: command.callId,
    requestedByUserId: command.actorUserId,
    participants: requestResult.value.participantUserIds,
    requestedAtUtc,
  };
  await deps.publisher.enqueue(event);

  const dto: CallRecordingConsentRequestedDto = {
    callId: command.callId,
    requestedByUserId: command.actorUserId,
    requestedAtUtc,
  };
  deps.emitter.emitToCall({
    tenantId: command.tenantId,
    callId: command.callId,
    event: CallSocketEvents.RecordingConsentRequested,
    envelope: {
      eventId: randomUUID(),
      correlationId: command.correlationId,
      emittedAtUtc: new Date().toISOString(),
      payload: dto,
    },
  });

  const result: RequestCallRecordingResult = {
    callId: command.callId,
    participantUserIds: requestResult.value.participantUserIds,
    requestedAtUtc,
  };
  await deps.idempotency.commit({
    tenantId: command.tenantId,
    userId: command.actorUserId,
    scope: 'call.recording.request',
    clientKey: command.clientKey,
    payload: result,
    token: reservation.token,
  });
  return Result.ok(result);
}
