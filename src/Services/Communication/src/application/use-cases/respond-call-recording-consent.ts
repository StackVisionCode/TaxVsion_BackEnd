import { randomUUID } from 'node:crypto';
import { Result, makeError } from '../../domain/shared/result.js';
import { RecordingScope } from '../../domain/recording/recording-session-state.js';
import type { RecordingConsentEntryStatus } from '../../domain/recording/recording-consent.js';
import type { CallRepository } from '../ports/call-repository.js';
import type { RecordingSessionRepository, RecordingConsentRepository } from '../ports/recording-repository.js';
import type { IntegrationEventPublisher } from '../ports/integration-event-publisher.js';
import type { RealtimeEmitter } from '../ports/realtime-emitter.js';
import { CallEventTypes, type CallRecordingConsentRecordedEvent } from '../../contracts/events/call-events.js';
import {
  CallSocketEvents,
  type CallRecordingStateChangedDto,
  type CallRecordingConsentRecordedDto,
} from '../../contracts/socket/call-socket-events.js';
import { startCallRecording } from './start-call-recording.js';
import { logger } from '../../infrastructure/logger/logger.js';

export interface RespondCallRecordingConsentCommand {
  readonly tenantId: string;
  readonly correlationId: string;
  readonly callId: string;
  readonly actorUserId: string;
  readonly response: 'Accepted' | 'Rejected';
}

/** Superset de StartCallRecordingDeps — se reenvia tal cual al auto-invoke. */
export interface RespondCallRecordingConsentDeps {
  readonly calls: CallRepository;
  readonly recordingSessions: RecordingSessionRepository;
  readonly recordingConsents: RecordingConsentRepository;
  readonly publisher: IntegrationEventPublisher;
  readonly emitter: RealtimeEmitter;
}

export async function respondCallRecordingConsent(
  command: RespondCallRecordingConsentCommand,
  deps: RespondCallRecordingConsentDeps,
): Promise<Result<{ response: RecordingConsentEntryStatus }>> {
  const call = await deps.calls.findById(command.tenantId, command.callId);
  if (!call) return Result.fail(makeError('Call.NotFound', 'Call not found.'));

  const session = await deps.recordingSessions.findByScope(command.tenantId, RecordingScope.Call, command.callId);
  if (!session) return Result.fail(makeError('RecordingSession.NotFound', 'No recording session for this call.'));
  if (session.sessionState !== 'Requesting') {
    return Result.fail(makeError('RecordingSession.NotRequesting', `Cannot respond while session is ${session.sessionState}.`));
  }

  const now = new Date();
  const consentResult = call.recordConsent({ userId: command.actorUserId, response: command.response, respondedAtUtc: now });
  if (!consentResult.isSuccess) return Result.fail(consentResult.error);
  await deps.recordingConsents.append(consentResult.value);

  const event: CallRecordingConsentRecordedEvent = {
    eventId: randomUUID(),
    eventType: CallEventTypes.RecordingConsentRecorded,
    tenantId: command.tenantId,
    correlationId: command.correlationId,
    occurredOnUtc: now.toISOString(),
    callId: command.callId,
    userId: command.actorUserId,
    response: command.response,
    respondedAtUtc: now.toISOString(),
  };
  await deps.publisher.enqueue(event);

  // Fase Frontend 4 — ver docblock del equivalente en
  // respond-meeting-recording-consent.ts: sin esto ningun participante se
  // entera de quien respondio que hasta que el estado agregado cambia.
  const consentRecordedDto: CallRecordingConsentRecordedDto = {
    callId: command.callId,
    userId: command.actorUserId,
    response: command.response,
    respondedAtUtc: now.toISOString(),
  };
  deps.emitter.emitToCall({
    tenantId: command.tenantId,
    callId: command.callId,
    event: CallSocketEvents.RecordingConsentRecorded,
    envelope: {
      eventId: randomUUID(),
      correlationId: command.correlationId,
      emittedAtUtc: new Date().toISOString(),
      payload: consentRecordedDto,
    },
  });

  const stateDto: CallRecordingStateChangedDto = {
    callId: command.callId,
    state: session.sessionState,
    updatedAtUtc: now.toISOString(),
  };
  deps.emitter.emitToCall({
    tenantId: command.tenantId,
    callId: command.callId,
    event: CallSocketEvents.RecordingStateChanged,
    envelope: {
      eventId: randomUUID(),
      correlationId: command.correlationId,
      emittedAtUtc: new Date().toISOString(),
      payload: stateDto,
    },
  });

  // Con policy AllAcceptedRequired y solo 2 partes, un Rejected nunca deja
  // arrancar (evaluateRecordingConsentPolicy exige Accepted explicito de
  // ambas). Igual se intenta siempre — si la respuesta fue Accepted y la
  // otra parte ya habia aceptado antes, esto es lo que efectivamente arranca.
  const startResult = await startCallRecording(
    { tenantId: command.tenantId, correlationId: command.correlationId, callId: command.callId, actorUserId: session.requestedByUserId },
    deps,
  );
  if (!startResult.isSuccess) {
    logger.debug({ err: startResult.error, callId: command.callId }, 'Auto-start after consent response did not proceed');
  }

  return Result.ok({ response: command.response });
}
