import { randomUUID } from 'node:crypto';
import type { AttachmentTrackingRepository } from '../ports/attachment-tracking-repository.js';
import type { RealtimeEmitter } from '../ports/realtime-emitter.js';
import type { IncomingEnvelope } from '../ports/event-consumer.js';
import { ChatSocketEvents, type AttachmentFlaggedDto } from '../../contracts/socket/chat-socket-events.js';

/**
 * Consumers de CloudStorage -> AttachmentTracking. Un adjunto de chat
 * (`AttachmentTracking`, registrado en `sendMessage` con status `Pending`)
 * pasa a `Available` cuando CloudStorage confirma el escaneo AV, o a
 * `Infected`/`Deleted`/`BlockedByPolicy` si fue bloqueado o purgado — en esos
 * casos se notifica al room de la conversacion via
 * `chat.message.attachment_flagged` para que el cliente pueda ocultar/
 * reemplazar el adjunto.
 *
 * `cloudstorage.file.pending_review.v1` (verdict Uncertain de IContentScanner)
 * NO se consume aca a proposito — es un estado de compliance interno sin
 * flujo de reviewer todavia (ver docblock de FilePendingReviewIntegrationEvent
 * en CloudStorage); no hay nada util que reflejar en el chat con el adjunto
 * todavia en revision, y el NoOpContentScanner de MVP nunca lo dispara.
 */
export function bindCloudStorageConsumers(
  register: (eventType: string, handler: (env: IncomingEnvelope) => Promise<void>) => void,
  deps: { attachmentTracking: AttachmentTrackingRepository; emitter: RealtimeEmitter },
): void {
  register('cloudstorage.file.available.v1', async (env) => {
    const fileId = getString(env.payload, 'fileId') ?? getString(env.payload, 'FileId');
    if (!fileId) return;
    await deps.attachmentTracking.markStatus({ fileId, status: 'Available' });
  });

  register('cloudstorage.file.infected.v1', (env) => flagAttachment(env, 'Infected', deps));
  register('cloudstorage.file.deleted.v1', (env) => flagAttachment(env, 'Deleted', deps));
  register('cloudstorage.file.blocked_by_policy.v1', (env) => flagAttachment(env, 'BlockedByPolicy', deps));
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
