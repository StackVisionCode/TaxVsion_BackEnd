import { describe, expect, it, vi } from 'vitest';
import { bindCloudStorageConsumers } from '../../src/application/event-handlers/cloudstorage-consumers.js';
import type {
  AttachmentTrackingRepository,
  AttachmentTrackingSnapshot,
} from '../../src/application/ports/attachment-tracking-repository.js';
import type { IncomingEnvelope } from '../../src/application/ports/event-consumer.js';
import type { NotificationRepository } from '../../src/application/ports/notification-repository.js';
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
    const notifications: NotificationRepository = {
      createIfMissing: vi.fn().mockResolvedValue(true),
      findById: vi.fn(),
      update: vi.fn(),
      listForUser: vi.fn(),
      countUnread: vi.fn().mockResolvedValue(0),
    };
    const emitter: RealtimeEmitter = {
      emitToConversation: vi.fn(),
      emitToCall: vi.fn(),
      emitToMeeting: vi.fn(),
      emitToUser: vi.fn(),
    } as unknown as RealtimeEmitter;

    bindCloudStorageConsumers(register, { attachmentTracking, notifications, emitter });
    return { handlers, attachmentTracking, notifications, emitter };
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

/**
 * Fase 1B — cloudstorage.file.available.v1 y cloudstorage.file.blocked_by_policy.v1
 * ADEMAS notifican al uploader (CreatedBy). Estos 2 eventos ya tenian un
 * handler registrado (attachment-tracking arriba), asi que la notificacion
 * vive en el MISMO callback — ConsumerRuntime.register solo admite un handler
 * por eventType.
 */
describe('bindCloudStorageConsumers — notificacion al uploader (Fase 1B)', () => {
  function setupWithNotifications() {
    const handlers = new Map<string, (env: IncomingEnvelope) => Promise<void>>();
    const register = (eventType: string, handler: (env: IncomingEnvelope) => Promise<void>) => {
      handlers.set(eventType, handler);
    };
    const attachmentTracking: AttachmentTrackingRepository = {
      register: vi.fn(),
      findByFileId: vi.fn().mockResolvedValue(null),
      markStatus: vi.fn(),
    };
    const notifications: NotificationRepository = {
      createIfMissing: vi.fn().mockResolvedValue(true),
      findById: vi.fn(),
      update: vi.fn(),
      listForUser: vi.fn(),
      countUnread: vi.fn().mockResolvedValue(0),
    };
    const emitter: RealtimeEmitter = {
      emitToConversation: vi.fn(),
      emitToCall: vi.fn(),
      emitToMeeting: vi.fn(),
      emitToUser: vi.fn(),
    } as unknown as RealtimeEmitter;

    bindCloudStorageConsumers(register, { attachmentTracking, notifications, emitter });
    return { handlers, notifications };
  }

  function envelopeWithCreatedBy(eventType: string, payload: Record<string, unknown>): IncomingEnvelope {
    return {
      eventId: 'evt-2',
      eventType,
      tenantId: 'tenant-1',
      correlationId: 'corr-1',
      occurredOnUtc: new Date().toISOString(),
      payload,
    };
  }

  it('file.available.v1 crea la notificacion para CreatedBy', async () => {
    const { handlers, notifications } = setupWithNotifications();

    await handlers.get('cloudstorage.file.available.v1')!(
      envelopeWithCreatedBy('cloudstorage.file.available.v1', {
        FileId: 'file-1',
        ObjectKey: 'k',
        ContentType: 'application/pdf',
        SizeBytes: 10,
        ChecksumSha256: 'x',
        CreatedBy: 'uploader-1',
      }),
    );

    expect(notifications.createIfMissing).toHaveBeenCalledTimes(1);
    expect(vi.mocked(notifications.createIfMissing).mock.calls[0]![0]!.userId).toBe('uploader-1');
  });

  it('file.blocked_by_policy.v1 crea la notificacion para CreatedBy', async () => {
    const { handlers, notifications } = setupWithNotifications();

    await handlers.get('cloudstorage.file.blocked_by_policy.v1')!(
      envelopeWithCreatedBy('cloudstorage.file.blocked_by_policy.v1', {
        FileId: 'file-2',
        ObjectKey: 'k',
        PolicyReason: 'nudity',
        CreatedBy: 'uploader-2',
      }),
    );

    expect(notifications.createIfMissing).toHaveBeenCalledTimes(1);
    expect(vi.mocked(notifications.createIfMissing).mock.calls[0]![0]!.userId).toBe('uploader-2');
  });

  it('no crea ninguna notificacion si CreatedBy falta (regresion)', async () => {
    const { handlers, notifications } = setupWithNotifications();

    await handlers.get('cloudstorage.file.available.v1')!(
      envelopeWithCreatedBy('cloudstorage.file.available.v1', { FileId: 'file-3' }),
    );

    expect(notifications.createIfMissing).not.toHaveBeenCalled();
  });
});
