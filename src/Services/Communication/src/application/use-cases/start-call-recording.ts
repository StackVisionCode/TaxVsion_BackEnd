import { randomUUID } from 'node:crypto';
import { Result, makeError } from '../../domain/shared/result.js';
import { RecordingScope } from '../../domain/recording/recording-session-state.js';
import { RecordingConsentPolicy, evaluateRecordingConsentPolicy } from '../../domain/recording/recording-consent.js';
import type { CallRepository } from '../ports/call-repository.js';
import type { RecordingSessionRepository, RecordingConsentRepository } from '../ports/recording-repository.js';
import type { IntegrationEventPublisher } from '../ports/integration-event-publisher.js';
import type { RealtimeEmitter } from '../ports/realtime-emitter.js';
import { CallEventTypes, type CallRecordingStartedEvent } from '../../contracts/events/call-events.js';
import { CallSocketEvents, type CallRecordingStateChangedDto } from '../../contracts/socket/call-socket-events.js';
import type { RecordingConsentSnapshotEntry } from '../../contracts/events/integration-event.js';

/**
 * Mismo criterio que start-meeting-recording.ts — NO se dispara directamente
 * desde un evento de socket, solo desde respond-call-recording-consent.ts
 * (auto-invoke) o desde el timeout scheduler (15s). Policy FIJA
 * AllAcceptedRequired (simplificacion de Fase Backend 4 — llamadas son
 * siempre 1:1, no hay setting de tenant que leer como en meetings).
 */
export interface StartCallRecordingCommand {
  readonly tenantId: string;
  readonly correlationId: string;
  readonly callId: string;
  /** Siempre RecordingSession.requestedByUserId — ya validado participante al pedir la grabacion. */
  readonly actorUserId: string;
}

export interface StartCallRecordingDeps {
  readonly calls: CallRepository;
  readonly recordingSessions: RecordingSessionRepository;
  readonly recordingConsents: RecordingConsentRepository;
  readonly publisher: IntegrationEventPublisher;
  readonly emitter: RealtimeEmitter;
}

export async function startCallRecording(
  command: StartCallRecordingCommand,
  deps: StartCallRecordingDeps,
): Promise<Result<{ startedAtUtc: string }>> {
  const call = await deps.calls.findById(command.tenantId, command.callId);
  if (!call) return Result.fail(makeError('Call.NotFound', 'Call not found.'));

  const session = await deps.recordingSessions.findByScope(command.tenantId, RecordingScope.Call, command.callId);
  if (!session) return Result.fail(makeError('RecordingSession.NotFound', 'No recording session for this call.'));
  if (session.sessionState !== 'Requesting') {
    return Result.fail(makeError('RecordingSession.NotRequesting', `Cannot start from ${session.sessionState}.`));
  }

  const allEntries = await deps.recordingConsents.listByScope(command.tenantId, RecordingScope.Call, command.callId);
  const latestByUser = new Map<string, RecordingConsentSnapshotEntry>();
  for (const entry of allEntries) {
    const snap = entry.toSnapshot();
    if (snap.response === 'Requested') continue;
    latestByUser.set(snap.userId, {
      userId: snap.userId,
      response: snap.response,
      respondedAtUtc: snap.respondedAtUtc.toISOString(),
    });
  }
  const consentSnapshot: readonly RecordingConsentSnapshotEntry[] = [...latestByUser.values()];

  const participantUserIds = [call.callerUserId, call.calleeUserId];
  const policyAllows = evaluateRecordingConsentPolicy({
    policy: RecordingConsentPolicy.AllAcceptedRequired,
    participantUserIds,
    requestedByUserId: session.requestedByUserId,
    consentEntries: consentSnapshot.map((e) => ({ userId: e.userId, response: e.response })),
  });
  if (!policyAllows) {
    return Result.fail(makeError('RecordingSession.ConsentPolicyNotSatisfied', 'Consent policy does not allow starting yet.'));
  }

  const startResult = call.startRecording({ actorUserId: command.actorUserId, session, policyAllows: true });
  if (!startResult.isSuccess) return Result.fail(startResult.error);
  await deps.recordingSessions.save(startResult.value);

  const snap = startResult.value.toSnapshot();
  const startedAtUtc = (snap.startedAtUtc ?? new Date()).toISOString();

  const event: CallRecordingStartedEvent = {
    eventId: randomUUID(),
    eventType: CallEventTypes.RecordingStarted,
    tenantId: command.tenantId,
    correlationId: command.correlationId,
    occurredOnUtc: startedAtUtc,
    callId: command.callId,
    startedByUserId: command.actorUserId,
    startedAtUtc,
    consentSnapshot,
  };
  await deps.publisher.enqueue(event);

  const dto: CallRecordingStateChangedDto = { callId: command.callId, state: 'Recording', updatedAtUtc: startedAtUtc };
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

  return Result.ok({ startedAtUtc });
}
