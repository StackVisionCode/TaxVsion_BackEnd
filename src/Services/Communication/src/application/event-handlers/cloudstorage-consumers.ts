import { randomUUID } from 'node:crypto';
import { pushAndEmitNotification } from '../use-cases/push-notification.js';
import type { AttachmentTrackingRepository } from '../ports/attachment-tracking-repository.js';
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
    // C16-b: gate T3.7 por grupo `CloudStoragePolicy`. El attachment-tracking NO se
    // gatea (es responsabilidad propia de Communication); solo la fila de la campana.
    inAppSourcePolicy: 'Communication' | 'Notification';
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
  });

  register('cloudstorage.file.infected.v1', (env) => flagAttachment(env, 'Infected', deps));
  register('cloudstorage.file.deleted.v1', (env) => flagAttachment(env, 'Deleted', deps));
  register('cloudstorage.file.blocked_by_policy.v1', async (env) => {
    await flagAttachment(env, 'BlockedByPolicy', deps);
    if (deps.inAppSourcePolicy === 'Communication') {
      await notifyFileBlockedByPolicy(env, deps);
    }
  });
}

async function notifyFileBlockedByPolicy(
  env: IncomingEnvelope,
  deps: { notifications: NotificationRepository; emitter: RealtimeEmitter },
): Promise<void> {
  const fileId = getString(env.payload, 'fileId') ?? getString(env.payload, 'FileId');
  const createdBy = getString(env.payload, 'createdBy') ?? getString(env.payload, 'CreatedBy');
  if (!fileId || !createdBy) return;
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
      title: 'File blocked by moderation',
      body: 'One of your files was blocked by the content moderation review.',
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
