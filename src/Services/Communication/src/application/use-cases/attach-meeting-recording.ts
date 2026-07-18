import { randomUUID } from 'node:crypto';
import { Result, makeError } from '../../domain/shared/result.js';
import { RecordingScope } from '../../domain/recording/recording-session-state.js';
import type { MeetingRepository } from '../ports/meeting-repository.js';
import type { RecordingSessionRepository } from '../ports/recording-repository.js';
import type { IdempotencyStore } from '../ports/idempotency-store.js';
import type { IntegrationEventPublisher } from '../ports/integration-event-publisher.js';
import type { RealtimeEmitter } from '../ports/realtime-emitter.js';
import type { CloudStorageMetadataClient } from '../ports/cloudstorage-metadata-client.js';
import { RecordingErrors } from './recording-errors.js';
import {
  MeetingEventTypes,
  type MeetingRecordingReadyEvent,
  type MeetingRecordingProcessingStartedEvent,
} from '../../contracts/events/meeting-events.js';
import {
  MeetingSocketEvents,
  type MeetingRecordingStateChangedDto,
} from '../../contracts/socket/meeting-socket-events.js';

/**
 * Comando: adjuntar una grabacion ya subida a CloudStorage a un meeting.
 * Mismo flujo que attach-call-recording.ts — el cliente sube el blob directo
 * a CloudStorage y manda el `fileId` resultante aca.
 *
 * Fase Backend 3 — dos caminos segun exista o no un RecordingSession:
 *  - LEGACY (sin session — meetings que nunca pasaron por request/consent,
 *    o creados antes de Fase Backend 2): comportamiento identico al de antes
 *    — cualquier past-participant puede attachar, publica RecordingReady
 *    directo, sin Processing intermedio. Preserva compat con el frontend viejo.
 *  - NUEVO (con session, siempre en Stopping en este punto): restringe a
 *    Host/Cohost, transiciona la session a Processing, publica
 *    RecordingProcessingStarted — RecordingReady se publica despues, cuando
 *    el transcript worker confirma (ver transcript-consumers.ts).
 */
export interface AttachMeetingRecordingCommand {
  readonly tenantId: string;
  readonly correlationId: string;
  readonly clientKey: string;
  readonly meetingId: string;
  readonly actorUserId: string;
  readonly fileId: string;
}

export interface AttachMeetingRecordingResult {
  readonly meetingId: string;
  readonly recordingFileId: string;
}

export interface AttachMeetingRecordingDeps {
  readonly meetings: MeetingRepository;
  readonly recordingSessions: RecordingSessionRepository;
  readonly idempotency: IdempotencyStore;
  readonly publisher: IntegrationEventPublisher;
  readonly emitter: RealtimeEmitter;
  readonly cloudStorageMetadata: CloudStorageMetadataClient;
}

export async function attachMeetingRecording(
  command: AttachMeetingRecordingCommand,
  deps: AttachMeetingRecordingDeps,
): Promise<Result<AttachMeetingRecordingResult>> {
  const reservation = await deps.idempotency.tryReserve<AttachMeetingRecordingResult>({
    tenantId: command.tenantId,
    userId: command.actorUserId,
    scope: 'meeting.recording.attach',
    clientKey: command.clientKey,
    ttlSeconds: 60,
  });
  if (reservation.status === 'replay') return Result.ok(reservation.payload);

  const release = () =>
    deps.idempotency.release({
      tenantId: command.tenantId,
      userId: command.actorUserId,
      scope: 'meeting.recording.attach',
      clientKey: command.clientKey,
      token: reservation.token,
    });

  const meeting = await deps.meetings.findById(command.tenantId, command.meetingId);
  if (!meeting) {
    await release();
    return Result.fail(makeError('Meeting.NotFound', 'Meeting not found.'));
  }

  // Fase Backend 8 (bug #245) — validar que el file no sea 0 bytes ANTES de
  // transicionar la RecordingSession a Processing. Sin este check, un upload
  // vacio (MediaRecorder sin tracks por permisos denegados) desencadenaba una
  // migracion Stopping→Processing→Failed muy costosa cuando el worker
  // rebotaba durante el transcode; ahora se rechaza al toque, sin tocar
  // estado, y el frontend puede mostrar el error especifico.
  const metadata = await deps.cloudStorageMetadata.getMetadata(command.tenantId, command.fileId);
  if (metadata === null) {
    await release();
    return Result.fail(makeError('Meeting.Recording.FileNotFound', `Recording file ${command.fileId} not found in CloudStorage.`));
  }
  if (metadata.sizeBytes <= 0) {
    await release();
    return Result.fail(makeError(RecordingErrors.MeetingEmptyFile.code, RecordingErrors.MeetingEmptyFile.message));
  }

  const session = await deps.recordingSessions.findByScope(command.tenantId, RecordingScope.Meeting, command.meetingId);

  if (session === null) {
    // ---------- LEGACY PATH ----------
    const wasParticipant = meeting.toSnapshot().participants.some((p) => p.userId === command.actorUserId);
    if (!wasParticipant) {
      await release();
      return Result.fail(makeError('Meeting.NotParticipant', 'User was not a participant of this meeting.'));
    }

    const attachResult = meeting.attachRecording(command.fileId);
    if (!attachResult.isSuccess) {
      await release();
      return Result.fail(attachResult.error);
    }
    await deps.meetings.save(meeting);

    const snapshot = meeting.toSnapshot();
    const event: MeetingRecordingReadyEvent = {
      eventId: randomUUID(),
      eventType: MeetingEventTypes.RecordingReady,
      tenantId: command.tenantId,
      correlationId: command.correlationId,
      occurredOnUtc: snapshot.updatedAtUtc.toISOString(),
      meetingId: snapshot.id,
      recordingFileId: command.fileId,
      durationSeconds: snapshot.durationSeconds ?? 0,
      participantCount: snapshot.participants.length,
      readyAtUtc: snapshot.updatedAtUtc.toISOString(),
    };
    await deps.publisher.enqueue(event);

    const result: AttachMeetingRecordingResult = { meetingId: snapshot.id, recordingFileId: command.fileId };
    await deps.idempotency.commit({
      tenantId: command.tenantId,
      userId: command.actorUserId,
      scope: 'meeting.recording.attach',
      clientKey: command.clientKey,
      payload: result,
      token: reservation.token,
    });
    return Result.ok(result);
  }

  // ---------- NEW PATH (RecordingSession existe) ----------
  const isHostOrCohost = meeting.hostUserId === command.actorUserId || meeting.isCohost(command.actorUserId);
  if (!isHostOrCohost) {
    await release();
    return Result.fail(makeError('Meeting.HostOnly', 'Only host or cohost can attach the recording.'));
  }

  const attachResult = meeting.attachRecording(command.fileId);
  if (!attachResult.isSuccess) {
    await release();
    return Result.fail(attachResult.error);
  }
  await deps.meetings.save(meeting);

  const processingResult = meeting.beginProcessingRecording({ session });
  if (!processingResult.isSuccess) {
    await release();
    return Result.fail(processingResult.error);
  }
  await deps.recordingSessions.save(processingResult.value);

  const startedAtUtc = new Date().toISOString();
  const processingEvent: MeetingRecordingProcessingStartedEvent = {
    eventId: randomUUID(),
    eventType: MeetingEventTypes.RecordingProcessingStarted,
    tenantId: command.tenantId,
    correlationId: command.correlationId,
    occurredOnUtc: startedAtUtc,
    meetingId: command.meetingId,
    recordingFileId: command.fileId,
    startedAtUtc,
  };
  await deps.publisher.enqueue(processingEvent);

  const dto: MeetingRecordingStateChangedDto = { meetingId: command.meetingId, state: 'Processing', updatedAtUtc: startedAtUtc };
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

  const result: AttachMeetingRecordingResult = { meetingId: command.meetingId, recordingFileId: command.fileId };
  await deps.idempotency.commit({
    tenantId: command.tenantId,
    userId: command.actorUserId,
    scope: 'meeting.recording.attach',
    clientKey: command.clientKey,
    payload: result,
    token: reservation.token,
  });
  return Result.ok(result);
}
