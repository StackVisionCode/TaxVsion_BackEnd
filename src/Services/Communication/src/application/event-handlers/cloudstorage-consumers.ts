import { randomUUID } from 'node:crypto';
import { pushNotification, type PushNotificationCommand } from '../use-cases/push-notification.js';
import type { AttachmentTrackingRepository } from '../ports/attachment-tracking-repository.js';
import type { NotificationRepository } from '../ports/notification-repository.js';
import type { RealtimeEmitter } from '../ports/realtime-emitter.js';
import type { IncomingEnvelope } from '../ports/event-consumer.js';
import { ChatSocketEvents, type AttachmentFlaggedDto } from '../../contracts/socket/chat-socket-events.js';
import { NotificationSocketEvents } from '../../contracts/socket/notification-socket-events.js';

/**
 * Consumers de CloudStorage -> AttachmentTracking + notificacion al dueño del
 * archivo. Un adjunto de chat (`AttachmentTracking`, registrado en
 * `sendMessage` con status `Pending`) pasa a `Available` cuando CloudStorage
 * confirma el escaneo AV, o a `Infected`/`Deleted`/`BlockedByPolicy` si fue
 * bloqueado o purgado — en esos casos se notifica al room de la conversacion
 * via `chat.message.attachment_flagged` para que el cliente pueda ocultar/
 * reemplazar el adjunto.
 *
 * `cloudstorage.file.available.v1` y `cloudstorage.file.blocked_by_policy.v1`
 * ADEMAS empujan una notificacion in-app al uploader (`CreatedBy`, Fase 1B del
 * plan de notificaciones) — viven en ESTE archivo, no en
 * `cloudstorage-notification-consumers.ts`, porque `ConsumerRuntime.register`
 * solo admite un handler por `eventType`: el handler de attachment-tracking ya
 * es dueño de ambos.
 *
 * `cloudstorage.file.pending_review.v1` (verdict Uncertain de IContentScanner)
 * NO se consume aca a proposito — es un estado de compliance interno sin
 * flujo de reviewer todavia (ver docblock de FilePendingReviewIntegrationEvent
 * en CloudStorage); no hay nada util que reflejar en el chat con el adjunto
 * todavia en revision, y el NoOpContentScanner de MVP nunca lo dispara.
 */
export function bindCloudStorageConsumers(
  register: (eventType: string, handler: (env: IncomingEnvelope) => Promise<void>) => void,
  deps: {
    attachmentTracking: AttachmentTrackingRepository;
    notifications: NotificationRepository;
    emitter: RealtimeEmitter;
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
      await deps.attachmentTracking.markStatus({ fileId, status: 'Available' });
    }
    await notifyFileAvailable(env, fileId, deps);
  });

  register('cloudstorage.file.infected.v1', (env) => flagAttachment(env, 'Infected', deps));
  register('cloudstorage.file.deleted.v1', (env) => flagAttachment(env, 'Deleted', deps));
  register('cloudstorage.file.blocked_by_policy.v1', async (env) => {
    await flagAttachment(env, 'BlockedByPolicy', deps);
    await notifyFileBlockedByPolicy(env, deps);
  });
}

async function notifyFileAvailable(
  env: IncomingEnvelope,
  fileId: string,
  deps: { notifications: NotificationRepository; emitter: RealtimeEmitter },
): Promise<void> {
  const createdBy = getString(env.payload, 'createdBy') ?? getString(env.payload, 'CreatedBy');
  if (!createdBy) return;
  await pushOwnerNotification(env, {
    userId: createdBy,
    kind: 'cloudstorage.file.available',
    priority: 'Normal',
    title: 'Archivo listo',
    body: 'Tu archivo termino de procesarse y ya esta disponible.',
    metadata: { fileId },
  }, deps);
}

async function notifyFileBlockedByPolicy(
  env: IncomingEnvelope,
  deps: { notifications: NotificationRepository; emitter: RealtimeEmitter },
): Promise<void> {
  const fileId = getString(env.payload, 'fileId') ?? getString(env.payload, 'FileId');
  const createdBy = getString(env.payload, 'createdBy') ?? getString(env.payload, 'CreatedBy');
  if (!fileId || !createdBy) return;
  await pushOwnerNotification(env, {
    userId: createdBy,
    kind: 'cloudstorage.file.blocked_by_policy',
    priority: 'High',
    title: 'Archivo bloqueado por moderacion',
    body: 'Uno de tus archivos fue bloqueado porque no paso la revision de contenido.',
    metadata: { fileId },
  }, deps);
}

async function pushOwnerNotification(
  env: IncomingEnvelope,
  input: Pick<PushNotificationCommand, 'userId' | 'kind' | 'priority' | 'title' | 'body' | 'metadata'>,
  deps: { notifications: NotificationRepository; emitter: RealtimeEmitter },
): Promise<void> {
  const result = await pushNotification(
    {
      tenantId: env.tenantId,
      userId: input.userId,
      kind: input.kind,
      ...(input.priority !== undefined ? { priority: input.priority } : {}),
      title: input.title,
      body: input.body,
      ...(input.metadata !== undefined ? { metadata: input.metadata } : {}),
      sourceEventId: env.eventId,
      sourceEventType: env.eventType,
      correlationId: env.correlationId ?? null,
    },
    deps,
  );
  if (result.isSuccess && result.value.created && result.value.notification) {
    deps.emitter.emitToUser({
      tenantId: env.tenantId,
      userId: input.userId,
      event: NotificationSocketEvents.Received,
      envelope: {
        eventId: randomUUID(),
        correlationId: env.correlationId ?? '',
        emittedAtUtc: new Date().toISOString(),
        payload: result.value.notification,
      },
    });
    deps.emitter.emitToUser({
      tenantId: env.tenantId,
      userId: input.userId,
      event: NotificationSocketEvents.UnreadCountChanged,
      envelope: {
        eventId: randomUUID(),
        correlationId: env.correlationId ?? '',
        emittedAtUtc: new Date().toISOString(),
        payload: { count: result.value.unreadCount },
      },
    });
  }
}

async function flagAttachment(
  env: IncomingEnvelope,
  status: 'Infected' | 'Deleted' | 'BlockedByPolicy',
  deps: { attachmentTracking: AttachmentTrackingRepository; emitter: RealtimeEmitter },
): Promise<void> {
  const fileId = getString(env.payload, 'fileId') ?? getString(env.payload, 'FileId');
  if (!fileId) return;
  const updated = await deps.attachmentTracking.markStatus({ fileId, status });
  if (!updated) return; // no registrado como adjunto de chat — nada que notificar.

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
