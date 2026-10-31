import { recordAttachmentMetadata } from '../use-cases/record-attachment-metadata.js';
import type { CloudStorageMetadataClient } from '../ports/cloudstorage-metadata-client.js';
import { randomUUID } from 'node:crypto';
import { pushAndEmitNotification } from '../use-cases/push-notification.js';
import type { AttachmentTrackingRepository } from '../ports/attachment-tracking-repository.js';
import type { MessageRepository } from '../ports/message-repository.js';
import type { NotificationRepository } from '../ports/notification-repository.js';
import type { RealtimeEmitter } from '../ports/realtime-emitter.js';
import type { IncomingEnvelope } from '../ports/event-consumer.js';
import { ChatSocketEvents, type AttachmentFlaggedDto } from '../../contracts/socket/chat-socket-events.js';

/**
 * Consumers de CloudStorage -> AttachmentTracking. Un adjunto de chat
 * (`AttachmentTracking`, registrado en
 * `sendMessage` con status `Pending`) pasa a `Available` cuando CloudStorage
 * confirma el escaneo AV, o a `Infected`/`Deleted`/`BlockedByPolicy` si fue
 * bloqueado o purgado — en esos casos se notifica al room de la conversacion
 * via `chat.message.attachment_flagged` para que el cliente pueda ocultar/
 * reemplazar el adjunto.
 *
 * `cloudstorage.file.blocked_by_policy.v1` tambien alerta al uploader. Vive en
 * ESTE archivo, no en
 * `cloudstorage-notification-consumers.ts`, porque `ConsumerRuntime.register`
 * solo admite un handler por `eventType`: el handler de attachment-tracking ya
 * es dueño del evento.
 *
 * `cloudstorage.file.pending_review.v1` (T-20, moderación de contenido): el adjunto
 * pasa a `PendingReview` ("Under review" en el chat) y se avisa al uploader. Si el
 * revisor lo aprueba llega `available` y se emite `Available` para volver a mostrarlo;
 * si lo rechaza llega `blocked_by_policy`.
 */
export function bindCloudStorageConsumers(
  register: (eventType: string, handler: (env: IncomingEnvelope) => Promise<void>) => void,
  deps: {
    attachmentTracking: AttachmentTrackingRepository;
    notifications: NotificationRepository;
    emitter: RealtimeEmitter;
    // C16-b: gate T3.7 por grupo `CloudStoragePolicy`. El attachment-tracking NO se
    // gatea (es responsabilidad propia de Communication); solo la fila de la campana.
    inAppSourcePolicy: 'Communication' | 'Notification';
    /** F7 (T-34): el archivo de una grabacion disponible o rechazado mueve su sesion a Ready/Failed. */
    recordingFiles?: RecordingFileStatusHandler | undefined;
    /** F8d: al quedar disponible se guarda el tipo DETECTADO y la categoría del adjunto. */
    cloudStorageMetadata?: CloudStorageMetadataClient | undefined;
    /** Para avisar a quien mandó el adjunto: el archivo lo crea la cuenta de servicio de Communication. */
    messages?: Pick<MessageRepository, 'findById'> | undefined;
  },
): void {
  register('cloudstorage.file.available.v1', async (env) => {
    const fileId = getString(env.payload, 'fileId') ?? getString(env.payload, 'FileId');
    if (!fileId) return;
    // cloudstorage.file.available.v1 se publica para CUALQUIER archivo del
    // tenant (grabaciones, transcripts, imports, firmas...), no solo adjuntos
    // de chat — a diferencia de flagAttachment() mas abajo, este handler no
    // chequeaba el retorno de markStatus() y el .update() de Prisma logueaba
    // un "Record to update not found" (nivel error, ruidoso) por cada archivo
    // que no fuera un adjunto trackeado. findByFileId() evita el update fallido.
    const tracked = await deps.attachmentTracking.findByFileId(fileId);
    if (tracked) {
      // Los clientes que mostraban "Scanning…" o "Under review" se enteran en vivo (E2E F8: quedaba
      // "Scanning…" hasta reabrir el hilo). Un evento repetido sobre uno ya disponible no avisa.
      if (tracked.status !== 'Available') await flagAttachment(env, 'Available', deps);
      await recordAttachmentMetadata({ tenantId: env.tenantId, fileId }, deps).catch(() => undefined);
    }
    if (isRecordingFolder(env)) await deps.recordingFiles?.onAvailable(env.tenantId, fileId, env.correlationId ?? null);
  });

  register('cloudstorage.file.infected.v1', async (env) => {
    await flagAttachment(env, 'Infected', deps);
    await rejectRecording(env, 'Infected', deps.recordingFiles);
  });
  register('cloudstorage.file.deleted.v1', (env) => flagAttachment(env, 'Deleted', deps));
  register('cloudstorage.file.blocked_by_policy.v1', async (env) => {
    await flagAttachment(env, 'BlockedByPolicy', deps);
    await rejectRecording(env, 'BlockedByPolicy', deps.recordingFiles);
    if (deps.inAppSourcePolicy === 'Communication') {
      await notifyFileBlockedByPolicy(env, deps);
    }
  });
  register('cloudstorage.file.pending_review.v1', async (env) => {
    await flagAttachment(env, 'PendingReview', deps);
    if (deps.inAppSourcePolicy === 'Communication') {
      await notifyFilePendingReview(env, deps);
    }
  });
  // Solo grabaciones: un adjunto de chat con escaneo fallido sigue su camino de siempre (sin cambio).
  register('cloudstorage.file.scan_failed.v1', (env) => rejectRecording(env, 'ScanFailed', deps.recordingFiles));
}

/** F7: lo que Communication hace cuando cambia el estado del archivo de una grabacion. */
export interface RecordingFileStatusHandler {
  onAvailable(tenantId: string, fileId: string, correlationId: string | null): Promise<void>;
  onRejected(tenantId: string, fileId: string, reason: string, correlationId: string | null): Promise<void>;
}

/** Solo compuestos (Recordings); las pistas (RecordingTracks) y los transcripts no deciden el estado. */
function isRecordingFolder(env: IncomingEnvelope): boolean {
  const folder = getString(env.payload, 'folderType') ?? getString(env.payload, 'FolderType');
  return folder === undefined || folder === 'Recordings';
}

async function rejectRecording(env: IncomingEnvelope, reason: string, handler: RecordingFileStatusHandler | undefined): Promise<void> {
  if (!handler || !isRecordingFolder(env)) return;
  const fileId = getString(env.payload, 'fileId') ?? getString(env.payload, 'FileId');
  if (!fileId) return;
  await handler.onRejected(env.tenantId, fileId, reason, env.correlationId ?? null);
}

async function notifyFileBlockedByPolicy(
  env: IncomingEnvelope,
  deps: NotifyDeps,
): Promise<void> {
  const fileId = getString(env.payload, 'fileId') ?? getString(env.payload, 'FileId');
  if (!fileId) return;
  const createdBy = await resolveUploader(env, fileId, deps);
  if (!createdBy) return;
  // Policy B del contrato Actor/Resource/Action: no mandamos actorDisplayName
  // (el front lo resuelve de su directory cache), ni titleKey/bodyKey (el front
  // tiene fallback por `kind` en notification-i18n.ts). Sí llenamos los campos
  // de dato: actor + resource + action + category, para que la fila sepa de qué
  // habla sin que el front tenga que adivinar desde `metadata`.
  await pushAndEmitNotification(
    env,
    {
      userId: createdBy,
      kind: 'cloudstorage.file.blocked_by_policy',
      priority: 'High',
      title: 'File blocked',
      body: 'One of your files was blocked because it goes against our content policy.',
      metadata: { fileId },
      category: 'DocumentsStorage',
      actorUserId: null,
      // El motor de moderación es automático — no hay usuario que haya bloqueado el archivo.
      actorType: 'System',
      resourceType: 'file',
      resourceId: fileId,
      action: 'review',
      occurredAtUtc: envOccurredAt(env),
    },
    deps,
  );
}

/** T-20: el archivo espera revisión humana; se avisa a quien lo subió. */
async function notifyFilePendingReview(
  env: IncomingEnvelope,
  deps: NotifyDeps,
): Promise<void> {
  const fileId = getString(env.payload, 'fileId') ?? getString(env.payload, 'FileId');
  if (!fileId) return;
  const createdBy = await resolveUploader(env, fileId, deps);
  if (!createdBy) return;
  await pushAndEmitNotification(
    env,
    {
      userId: createdBy,
      kind: 'cloudstorage.file.pending_review',
      priority: 'Normal',
      title: 'File under review',
      body: 'One of your files is being reviewed against our content policy. It will be available once approved.',
      metadata: { fileId },
      category: 'DocumentsStorage',
      actorUserId: null,
      actorType: 'System',
      resourceType: 'file',
      resourceId: fileId,
      action: 'review',
      occurredAtUtc: envOccurredAt(env),
    },
    deps,
  );
}

type NotifyDeps = {
  notifications: NotificationRepository;
  emitter: RealtimeEmitter;
  attachmentTracking: AttachmentTrackingRepository;
  messages?: Pick<MessageRepository, 'findById'> | undefined;
};

/**
 * Quién subió el archivo. Un adjunto de chat lo crea Communication con su token M2M, así que
 * `CreatedBy` es la cuenta de servicio (E2E F8: la campana no le llegaba a nadie): manda el remitente.
 */
async function resolveUploader(env: IncomingEnvelope, fileId: string, deps: NotifyDeps): Promise<string | undefined> {
  const createdBy = getString(env.payload, 'createdBy') ?? getString(env.payload, 'CreatedBy');
  const tracked = deps.messages ? await deps.attachmentTracking.findByFileId(fileId) : null;
  if (!tracked || !deps.messages) return createdBy;
  const message = await deps.messages.findById(tracked.tenantId, tracked.messageId);
  return message?.senderId ?? createdBy;
}

async function flagAttachment(
  env: IncomingEnvelope,
  status: AttachmentFlaggedDto['status'],
  deps: { attachmentTracking: AttachmentTrackingRepository; emitter: RealtimeEmitter },
): Promise<void> {
  const fileId = getString(env.payload, 'fileId') ?? getString(env.payload, 'FileId');
  if (!fileId) return;
  // Igual que en available: borrar un avatar u otro archivo no-adjunto no debe
  // disparar el update fallido de Prisma (log de error ruidoso).
  if (!(await deps.attachmentTracking.findByFileId(fileId))) return;
  const updated = await deps.attachmentTracking.markStatus({ fileId, status });
  if (!updated) return;

  const dto: AttachmentFlaggedDto = {
    messageId: updated.messageId,
    conversationId: updated.conversationId,
    fileId,
    status,
    flaggedAtUtc: updated.updatedAtUtc.toISOString(),
  };

  deps.emitter.emitToConversation({
    tenantId: updated.tenantId,
    conversationId: updated.conversationId,
    event: ChatSocketEvents.AttachmentFlagged,
    envelope: {
      eventId: randomUUID(),
      correlationId: env.correlationId ?? '',
      emittedAtUtc: new Date().toISOString(),
      payload: dto,
    },
  });
}

function getString(source: Record<string, unknown>, key: string): string | undefined {
  const value = source[key];
  return typeof value === 'string' ? value : undefined;
}

/**
 * `OccurredOn` del envelope .NET: hora REAL del hecho (p.ej. cuándo CloudStorage
 * bloqueó el archivo), distinta de `CreatedAtUtc` del repo que es la hora de
 * recepción. Fallback a now() si el publisher no lo mandó o es inválido.
 */
function envOccurredAt(env: IncomingEnvelope): Date {
  const raw = getString(env.payload, 'occurredOn') ?? getString(env.payload, 'OccurredOn');
  if (!raw) return new Date();
  const parsed = new Date(raw);
  return Number.isFinite(parsed.getTime()) ? parsed : new Date();
}
