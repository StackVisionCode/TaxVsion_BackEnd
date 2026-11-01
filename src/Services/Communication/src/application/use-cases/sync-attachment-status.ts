import { randomUUID } from 'node:crypto';
import type { AttachmentTrackingRepository, AttachmentTrackingStatus } from '../ports/attachment-tracking-repository.js';
import type { CloudStorageFileMetadata, CloudStorageMetadataClient } from '../ports/cloudstorage-metadata-client.js';
import type { RealtimeEmitter } from '../ports/realtime-emitter.js';
import { ChatSocketEvents, type AttachmentFlaggedDto } from '../../contracts/socket/chat-socket-events.js';

/** Estados finales del escaneo en CloudStorage que el tracking puede copiar. */
const FROM_STORAGE: Readonly<Record<string, AttachmentFlaggedDto['status']>> = {
  Available: 'Available',
  PendingReview: 'PendingReview',
  Infected: 'Infected',
  BlockedByPolicy: 'BlockedByPolicy',
};

/**
 * Un adjunto `Pending` toma el estado real de CloudStorage (fuente autoritativa del escaneo). Cubre el
 * evento `cloudstorage.file.available.v1` perdido o adelantado: un audio se escanea en < 1 s y el evento
 * puede llegar antes de que el mensaje registre el adjunto, que entonces quedaba `Pending` para siempre
 * (el play respondía 409). Avisa a la conversación si cambia. Devuelve el estado vigente (null = no es
 * un adjunto conocido).
 */
export async function syncAttachmentStatusFromStorage(
  input: { tenantId: string; fileId: string; known?: CloudStorageFileMetadata | null },
  deps: {
    attachmentTracking: AttachmentTrackingRepository;
    cloudStorageMetadata?: CloudStorageMetadataClient | undefined;
    emitter?: RealtimeEmitter | undefined;
  },
): Promise<AttachmentTrackingStatus | null> {
  const tracking = await deps.attachmentTracking.findByFileId(input.fileId);
  if (!tracking || tracking.status !== 'Pending') return tracking?.status ?? null;

  const meta =
    input.known ?? (deps.cloudStorageMetadata ? await deps.cloudStorageMetadata.getMetadata(input.tenantId, input.fileId).catch(() => null) : null);
  const next = meta?.status ? FROM_STORAGE[meta.status] : undefined;
  if (!next) return 'Pending';

  const updated = await deps.attachmentTracking.markStatus({ fileId: input.fileId, status: next }).catch(() => null);
  if (updated && deps.emitter) {
    const payload: AttachmentFlaggedDto = {
      messageId: updated.messageId,
      conversationId: updated.conversationId,
      fileId: input.fileId,
      status: next,
      flaggedAtUtc: updated.updatedAtUtc.toISOString(),
    };
    deps.emitter.emitToConversation({
      tenantId: updated.tenantId,
      conversationId: updated.conversationId,
      event: ChatSocketEvents.AttachmentFlagged,
      envelope: { eventId: randomUUID(), correlationId: '', emittedAtUtc: new Date().toISOString(), payload },
    });
  }
  return next;
}
