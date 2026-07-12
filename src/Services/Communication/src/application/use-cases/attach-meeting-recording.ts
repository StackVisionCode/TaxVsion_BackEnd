import { randomUUID } from 'node:crypto';
import { Result, makeError } from '../../domain/shared/result.js';
import type { MeetingRepository } from '../ports/meeting-repository.js';
import type { IdempotencyStore } from '../ports/idempotency-store.js';
import type { IntegrationEventPublisher } from '../ports/integration-event-publisher.js';
import { MeetingEventTypes, type MeetingRecordingReadyEvent } from '../../contracts/events/meeting-events.js';

/**
 * Comando: adjuntar una grabacion ya subida a CloudStorage a un meeting.
 * Mismo flujo que attach-call-recording.ts — el cliente sube el blob directo
 * a CloudStorage y manda el `fileId` resultante aca.
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
  readonly idempotency: IdempotencyStore;
  readonly publisher: IntegrationEventPublisher;
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
  // Cualquier participante que alguna vez estuvo en el meeting puede subir la
  // grabacion (no solo el host) — ej. un cohost que corrio la grabacion local.
  const wasParticipant = meeting
    .toSnapshot()
    .participants.some((p) => p.userId === command.actorUserId);
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
