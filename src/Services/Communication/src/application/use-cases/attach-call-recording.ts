import { randomUUID } from 'node:crypto';
import { Result, makeError } from '../../domain/shared/result.js';
import type { CallRepository } from '../ports/call-repository.js';
import type { IdempotencyStore } from '../ports/idempotency-store.js';
import type { IntegrationEventPublisher } from '../ports/integration-event-publisher.js';
import { CallEventTypes, type CallRecordingReadyEvent } from '../../contracts/events/call-events.js';

/**
 * Comando: adjuntar una grabacion ya subida a CloudStorage a una llamada. El
 * cliente graba con MediaRecorder, sube el blob directo a CloudStorage (fuera
 * de este servicio, mismo patron que attachmentFileId en chat), y manda el
 * `fileId` resultante aca. Publica CallRecordingReadyEvent para que
 * Notification/Analytics reaccionen.
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
  readonly idempotency: IdempotencyStore;
  readonly publisher: IntegrationEventPublisher;
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
