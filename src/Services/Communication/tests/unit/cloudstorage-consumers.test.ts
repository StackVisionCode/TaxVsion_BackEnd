import { describe, expect, it, vi } from 'vitest';
import { bindCloudStorageConsumers } from '../../src/application/event-handlers/cloudstorage-consumers.js';
import type {
  AttachmentTrackingRepository,
  AttachmentTrackingSnapshot,
} from '../../src/application/ports/attachment-tracking-repository.js';
import type { IncomingEnvelope } from '../../src/application/ports/event-consumer.js';
import type { RealtimeEmitter } from '../../src/application/ports/realtime-emitter.js';

function envelope(fileId: string): IncomingEnvelope {
  return {
    eventId: 'evt-1',
    eventType: 'cloudstorage.file.available.v1',
    tenantId: 'tenant-1',
    occurredOnUtc: new Date().toISOString(),
    payload: { fileId },
  };
}

/**
 * cloudstorage.file.available.v1 se publica para CUALQUIER archivo del
 * tenant (grabaciones, transcripts, imports, firmas...), no solo adjuntos
 * de chat trackeados en AttachmentTracking — antes del fix, el handler
 * llamaba markStatus() sin chequear si el fileId estaba registrado, y el
 * .update() de Prisma logueaba un "Record to update not found" (nivel
 * error, ruidoso pero no fatal gracias al .catch(() => null) del repo) por
 * cada archivo no-adjunto que pasara a Available.
 */
describe('bindCloudStorageConsumers — cloudstorage.file.available.v1', () => {
  function setup() {
    const handlers = new Map<string, (env: IncomingEnvelope) => Promise<void>>();
    const register = (eventType: string, handler: (env: IncomingEnvelope) => Promise<void>) => {
      handlers.set(eventType, handler);
    };
    const attachmentTracking: AttachmentTrackingRepository = {
      register: vi.fn(),
      findByFileId: vi.fn(),
      markStatus: vi.fn(),
    };
    const emitter: RealtimeEmitter = {
      emitToConversation: vi.fn(),
      emitToCall: vi.fn(),
      emitToMeeting: vi.fn(),
    } as unknown as RealtimeEmitter;

    bindCloudStorageConsumers(register, { attachmentTracking, emitter });
    return { handlers, attachmentTracking, emitter };
  }

  it('does not call markStatus for a fileId that is not a tracked chat attachment', async () => {
    const { handlers, attachmentTracking } = setup();
    vi.mocked(attachmentTracking.findByFileId).mockResolvedValue(null);

    await handlers.get('cloudstorage.file.available.v1')!(envelope('recording-file-id'));

    expect(attachmentTracking.findByFileId).toHaveBeenCalledWith('recording-file-id');
    expect(attachmentTracking.markStatus).not.toHaveBeenCalled();
  });

  it('marks the attachment Available when the fileId IS a tracked chat attachment', async () => {
    const { handlers, attachmentTracking } = setup();
    const snapshot: AttachmentTrackingSnapshot = {
      fileId: 'attachment-file-id',
      messageId: 'message-1',
      conversationId: 'conversation-1',
      tenantId: 'tenant-1',
      status: 'Pending',
      updatedAtUtc: new Date(),
    };
    vi.mocked(attachmentTracking.findByFileId).mockResolvedValue(snapshot);
    vi.mocked(attachmentTracking.markStatus).mockResolvedValue({ ...snapshot, status: 'Available' });

    await handlers.get('cloudstorage.file.available.v1')!(envelope('attachment-file-id'));

    expect(attachmentTracking.markStatus).toHaveBeenCalledWith({
      fileId: 'attachment-file-id',
      status: 'Available',
    });
  });
});
