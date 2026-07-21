import { randomUUID } from 'node:crypto';
import { Result, makeError } from '../../domain/shared/result.js';
import { RecordingScope } from '../../domain/recording/recording-session-state.js';
import type { CallRepository } from '../ports/call-repository.js';
import type { RecordingSessionRepository } from '../ports/recording-repository.js';
import type { IdempotencyStore } from '../ports/idempotency-store.js';
import type { IntegrationEventPublisher } from '../ports/integration-event-publisher.js';
import type { RealtimeEmitter } from '../ports/realtime-emitter.js';
import type { CloudStorageMetadataClient } from '../ports/cloudstorage-metadata-client.js';
import { RecordingErrors } from './recording-errors.js';
import {
  CallEventTypes,
  type CallRecordingReadyEvent,
  type CallRecordingProcessingStartedEvent,
} from '../../contracts/events/call-events.js';
import { CallSocketEvents, type CallRecordingStateChangedDto } from '../../contracts/socket/call-socket-events.js';

/**
 * Comando: adjuntar una grabacion ya subida a CloudStorage a una llamada. El
 * cliente graba con MediaRecorder, sube el blob directo a CloudStorage (fuera
 * de este servicio, mismo patron que attachmentFileId en chat), y manda el
 * `fileId` resultante aca.
 *
 * Fase Backend 4 — dos caminos segun exista o no un RecordingSession:
 *  - LEGACY (sin session): comportamiento identico al de antes — cualquier
 *    participante (caller o callee) puede attachar, publica RecordingReady
 *    directo. Preserva compat con el frontend viejo.
 *  - NUEVO (con session, siempre en Stopping en este punto): restringe a
 *    quien pidio la grabacion (`session.requestedByUserId`) — mas estricto
 *    que meetings (Host/Cohost), porque en una call de 2 no hay "cohost" y
 *    la contraparte no tiene por que poder subir el archivo por el otro.
 *    Transiciona a Processing, publica RecordingProcessingStarted —
 *    RecordingReady se publica despues, cuando el transcript worker confirma.
 */
export interface AttachCallRecordingCommand {
  readonly tenantId: string;
  readonly correlationId: string;
  readonly clientKey: string;
  readonly callId: string;
  readonly actorUserId: string;
  readonly fileId: string;
}

export interface AttachCallRecordingResult {
  readonly callId: string;
  readonly recordingFileId: string;
}

export interface AttachCallRecordingDeps {
  readonly calls: CallRepository;
  readonly recordingSessions: RecordingSessionRepository;
  readonly idempotency: IdempotencyStore;
  readonly publisher: IntegrationEventPublisher;
  readonly emitter: RealtimeEmitter;
  readonly cloudStorageMetadata: CloudStorageMetadataClient;
}

export async function attachCallRecording(
  command: AttachCallRecordingCommand,
  deps: AttachCallRecordingDeps,
): Promise<Result<AttachCallRecordingResult>> {
  const reservation = await deps.idempotency.tryReserve<AttachCallRecordingResult>({
    tenantId: command.tenantId,
    userId: command.actorUserId,
    scope: 'call.recording.attach',
    clientKey: command.clientKey,
    ttlSeconds: 60,
  });
  if (reservation.status === 'replay') return Result.ok(reservation.payload);

  const release = () =>
    deps.idempotency.release({
      tenantId: command.tenantId,
      userId: command.actorUserId,
      scope: 'call.recording.attach',
      clientKey: command.clientKey,
      token: reservation.token,
    });

  const call = await deps.calls.findById(command.tenantId, command.callId);
  if (!call) {
    await release();
    return Result.fail(makeError('Call.NotFound', 'Call not found.'));
  }

  // Fase Backend 8 (bug #245) — mismo criterio que attach-meeting-recording.ts.
  const metadata = await deps.cloudStorageMetadata.getMetadata(command.tenantId, command.fileId);
  if (metadata === null) {
    await release();
    return Result.fail(makeError('Call.Recording.FileNotFound', `Recording file ${command.fileId} not found in CloudStorage.`));
  }
  if (metadata.sizeBytes <= 0) {
    await release();
    return Result.fail(makeError(RecordingErrors.CallEmptyFile.code, RecordingErrors.CallEmptyFile.message));
  }

  const session = await deps.recordingSessions.findByScope(command.tenantId, RecordingScope.Call, command.callId);

  if (session === null) {
    // ---------- LEGACY PATH ----------
    if (!call.isParticipant(command.actorUserId)) {
      await release();
      return Result.fail(makeError('Call.NotParticipant', 'User is not a participant of this call.'));
    }

    const attachResult = call.attachRecording(command.fileId);
    if (!attachResult.isSuccess) {
      await release();
      return Result.fail(attachResult.error);
    }
    await deps.calls.save(call);

    const snapshot = call.toSnapshot();
    const event: CallRecordingReadyEvent = {
      eventId: randomUUID(),
      eventType: CallEventTypes.RecordingReady,
      tenantId: command.tenantId,
      correlationId: command.correlationId,
      occurredOnUtc: snapshot.updatedAtUtc.toISOString(),
      callId: snapshot.id,
      recordingFileId: command.fileId,
      durationSeconds: snapshot.durationSeconds ?? 0,
      readyAtUtc: snapshot.updatedAtUtc.toISOString(),
      callerUserId: snapshot.callerUserId,
      calleeUserId: snapshot.calleeUserId,
    };
    await deps.publisher.enqueue(event);

    const result: AttachCallRecordingResult = { callId: snapshot.id, recordingFileId: command.fileId };
    await deps.idempotency.commit({
      tenantId: command.tenantId,
      userId: command.actorUserId,
      scope: 'call.recording.attach',
      clientKey: command.clientKey,
      payload: result,
      token: reservation.token,
    });
    return Result.ok(result);
  }

  // ---------- NEW PATH (RecordingSession existe) ----------
  if (session.requestedByUserId !== command.actorUserId) {
    await release();
    return Result.fail(makeError('Call.RecordingRequesterOnly', 'Only who requested the recording can attach it.'));
  }

  const attachResult = call.attachRecording(command.fileId);
  if (!attachResult.isSuccess) {
    await release();
    return Result.fail(attachResult.error);
  }
  await deps.calls.save(call);

  const processingResult = call.beginProcessingRecording({ session });
  if (!processingResult.isSuccess) {
    await release();
    return Result.fail(processingResult.error);
  }
  await deps.recordingSessions.save(processingResult.value);

  const startedAtUtc = new Date().toISOString();
  const processingEvent: CallRecordingProcessingStartedEvent = {
    eventId: randomUUID(),
    eventType: CallEventTypes.RecordingProcessingStarted,
    tenantId: command.tenantId,
    correlationId: command.correlationId,
    occurredOnUtc: startedAtUtc,
    callId: command.callId,
    recordingFileId: command.fileId,
    startedAtUtc,
  };
  await deps.publisher.enqueue(processingEvent);

  const dto: CallRecordingStateChangedDto = { callId: command.callId, state: 'Processing', updatedAtUtc: startedAtUtc };
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

  const result: AttachCallRecordingResult = { callId: command.callId, recordingFileId: command.fileId };
  await deps.idempotency.commit({
    tenantId: command.tenantId,
    userId: command.actorUserId,
    scope: 'call.recording.attach',
    clientKey: command.clientKey,
    payload: result,
    token: reservation.token,
  });
  return Result.ok(result);
}
