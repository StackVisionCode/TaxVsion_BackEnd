import { describe, expect, it, vi } from 'vitest';
import { bindCloudStorageConsumers } from '../../src/application/event-handlers/cloudstorage-consumers.js';
import type {
  AttachmentTrackingRepository,
  AttachmentTrackingSnapshot,
} from '../../src/application/ports/attachment-tracking-repository.js';
import type { IncomingEnvelope } from '../../src/application/ports/event-consumer.js';
import type { NotificationRepository } from '../../src/application/ports/notification-repository.js';
import type { RealtimeEmitter } from '../../src/application/ports/realtime-emitter.js';
import type { Message } from '../../src/domain/conversations/message.js';

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
      markRead: vi.fn().mockResolvedValue(true),
      dismiss: vi.fn().mockResolvedValue(true),
      markAllRead: vi.fn().mockResolvedValue({ markedCount: 0, unreadCount: 0 }),
      listForUser: vi.fn(),
      countUnread: vi.fn().mockResolvedValue(0),
      markUnread: vi.fn().mockResolvedValue(true),
      markManyRead: vi.fn().mockResolvedValue({ markedCount: 0, unreadCount: 0 }),
      markManyUnread: vi.fn().mockResolvedValue({ unmarkedCount: 0, unreadCount: 0 }),
      dismissMany: vi.fn().mockResolvedValue({ dismissedCount: 0, unreadCount: 0 }),
      countForUser: vi.fn().mockResolvedValue(0),
    listSinceForUser: vi.fn().mockResolvedValue([]),
    };
    const emitter: RealtimeEmitter = {
      emitToConversation: vi.fn(),
      emitToCall: vi.fn(),
      emitToMeeting: vi.fn(),
      emitToUser: vi.fn(),
    } as unknown as RealtimeEmitter;

    bindCloudStorageConsumers(register, {
      attachmentTracking,
      notifications,
      emitter,
      inAppSourcePolicy: 'Communication',
    });
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
    const { handlers, attachmentTracking, emitter } = setup();
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
    // E2E F8: el hilo dejaba "Scanning…" hasta reabrirlo; ahora se avisa a la conversación.
    const call = vi.mocked(emitter.emitToConversation).mock.calls[0]![0];
    expect((call.envelope.payload as { status: string }).status).toBe('Available');
  });

  // Al reemplazar un avatar se borra el anterior y llega file.deleted de un archivo no-adjunto.
  it('does not call markStatus nor emit when a non-attachment file is deleted', async () => {
    const { handlers, attachmentTracking, emitter } = setup();
    vi.mocked(attachmentTracking.findByFileId).mockResolvedValue(null);

    await handlers.get('cloudstorage.file.deleted.v1')?.(envelope('old-avatar-file-id'));

    expect(attachmentTracking.findByFileId).toHaveBeenCalledWith('old-avatar-file-id');
    expect(attachmentTracking.markStatus).not.toHaveBeenCalled();
    expect(emitter.emitToConversation).not.toHaveBeenCalled();
  });

  it('flags a tracked chat attachment as Deleted and notifies the conversation', async () => {
    const { handlers, attachmentTracking, emitter } = setup();
    const snapshot: AttachmentTrackingSnapshot = {
      fileId: 'attachment-file-id',
      messageId: 'message-1',
      conversationId: 'conversation-1',
      tenantId: 'tenant-1',
      status: 'Available',
      updatedAtUtc: new Date(),
    };
    vi.mocked(attachmentTracking.findByFileId).mockResolvedValue(snapshot);
    vi.mocked(attachmentTracking.markStatus).mockResolvedValue({ ...snapshot, status: 'Deleted' });

    await handlers.get('cloudstorage.file.deleted.v1')?.(envelope('attachment-file-id'));

    expect(attachmentTracking.markStatus).toHaveBeenCalledWith({ fileId: 'attachment-file-id', status: 'Deleted' });
    expect(emitter.emitToConversation).toHaveBeenCalledTimes(1);
  });

  // T-20: la moderación deja el adjunto en revisión y el chat lo muestra como "Under review".
  it('flags a tracked attachment as PendingReview and tells the conversation', async () => {
    const { handlers, attachmentTracking, emitter } = setup();
    const snapshot: AttachmentTrackingSnapshot = {
      fileId: 'attachment-file-id',
      messageId: 'message-1',
      conversationId: 'conversation-1',
      tenantId: 'tenant-1',
      status: 'Pending',
      updatedAtUtc: new Date(),
    };
    vi.mocked(attachmentTracking.findByFileId).mockResolvedValue(snapshot);
    vi.mocked(attachmentTracking.markStatus).mockResolvedValue({ ...snapshot, status: 'PendingReview' });

    await handlers.get('cloudstorage.file.pending_review.v1')!(envelope('attachment-file-id'));

    expect(attachmentTracking.markStatus).toHaveBeenCalledWith({ fileId: 'attachment-file-id', status: 'PendingReview' });
    const call = vi.mocked(emitter.emitToConversation).mock.calls[0]![0];
    expect((call.envelope.payload as { status: string }).status).toBe('PendingReview');
  });

  // Aprobado por el revisor: vuelve a mostrarse en los clientes que ya tenían "Under review".
  it('emits Available when a reviewed attachment is approved', async () => {
    const { handlers, attachmentTracking, emitter } = setup();
    const snapshot: AttachmentTrackingSnapshot = {
      fileId: 'attachment-file-id',
      messageId: 'message-1',
      conversationId: 'conversation-1',
      tenantId: 'tenant-1',
      status: 'PendingReview',
      updatedAtUtc: new Date(),
    };
    vi.mocked(attachmentTracking.findByFileId).mockResolvedValue(snapshot);
    vi.mocked(attachmentTracking.markStatus).mockResolvedValue({ ...snapshot, status: 'Available' });

    await handlers.get('cloudstorage.file.available.v1')!(envelope('attachment-file-id'));

    expect(attachmentTracking.markStatus).toHaveBeenCalledTimes(1);
    const call = vi.mocked(emitter.emitToConversation).mock.calls[0]![0];
    expect((call.envelope.payload as { status: string }).status).toBe('Available');
  });
});

describe('bindCloudStorageConsumers — notificacion al uploader', () => {
  function setupWithNotifications(
    inAppSourcePolicy: 'Communication' | 'Notification' = 'Communication',
    chat?: { messageId: string; senderId: string },
  ) {
    const handlers = new Map<string, (env: IncomingEnvelope) => Promise<void>>();
    const register = (eventType: string, handler: (env: IncomingEnvelope) => Promise<void>) => {
      handlers.set(eventType, handler);
    };
    const attachmentTracking: AttachmentTrackingRepository = {
      register: vi.fn(),
      findByFileId: vi.fn().mockResolvedValue(
        chat
          ? { fileId: 'f', messageId: chat.messageId, conversationId: 'c', tenantId: 'tenant-1', status: 'Pending', updatedAtUtc: new Date() }
          : null,
      ),
      markStatus: vi.fn().mockResolvedValue(null),
    };
    const messages = {
      findById: vi.fn(async (_tenantId: string, messageId: string) =>
        chat && messageId === chat.messageId ? ({ senderId: chat.senderId } as Message) : null,
      ),
    };
    const notifications: NotificationRepository = {
      createIfMissing: vi.fn().mockResolvedValue(true),
      findById: vi.fn(),
      markRead: vi.fn().mockResolvedValue(true),
      dismiss: vi.fn().mockResolvedValue(true),
      markAllRead: vi.fn().mockResolvedValue({ markedCount: 0, unreadCount: 0 }),
      listForUser: vi.fn(),
      countUnread: vi.fn().mockResolvedValue(0),
      markUnread: vi.fn().mockResolvedValue(true),
      markManyRead: vi.fn().mockResolvedValue({ markedCount: 0, unreadCount: 0 }),
      markManyUnread: vi.fn().mockResolvedValue({ unmarkedCount: 0, unreadCount: 0 }),
      dismissMany: vi.fn().mockResolvedValue({ dismissedCount: 0, unreadCount: 0 }),
      countForUser: vi.fn().mockResolvedValue(0),
    listSinceForUser: vi.fn().mockResolvedValue([]),
    };
    const emitter: RealtimeEmitter = {
      emitToConversation: vi.fn(),
      emitToCall: vi.fn(),
      emitToMeeting: vi.fn(),
      emitToUser: vi.fn(),
    } as unknown as RealtimeEmitter;

    bindCloudStorageConsumers(register, {
      attachmentTracking,
      notifications,
      emitter,
      inAppSourcePolicy,
      messages,
    });
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

  it('file.available.v1 actualiza tracking sin crear ruido en la campana', async () => {
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

    expect(notifications.createIfMissing).not.toHaveBeenCalled();
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

  // C16-b: con el grupo CloudStoragePolicy apuntando a Notification, el handler Node
  // ya no crea la fila (lo hace Notification con audiencia ByPermission). El
  // attachment-tracking tiene que seguir funcionando aunque la notificacion se apague.
  it('file.blocked_by_policy.v1 NO notifica cuando el flag apunta a Notification', async () => {
    const { handlers, notifications } = setupWithNotifications('Notification');

    await handlers.get('cloudstorage.file.blocked_by_policy.v1')!(
      envelopeWithCreatedBy('cloudstorage.file.blocked_by_policy.v1', {
        FileId: 'file-2b',
        ObjectKey: 'k',
        PolicyReason: 'nudity',
        CreatedBy: 'uploader-2b',
      }),
    );

    expect(notifications.createIfMissing).not.toHaveBeenCalled();
  });

  it('file.pending_review.v1 avisa al uploader que su archivo está en revisión', async () => {
    const { handlers, notifications } = setupWithNotifications();

    await handlers.get('cloudstorage.file.pending_review.v1')!(
      envelopeWithCreatedBy('cloudstorage.file.pending_review.v1', {
        FileId: 'file-4',
        ObjectKey: 'k',
        Reason: 'Possible sexual content (score 0.700).',
        CreatedBy: 'uploader-4',
      }),
    );

    const row = vi.mocked(notifications.createIfMissing).mock.calls[0]![0]!;
    expect(row.userId).toBe('uploader-4');
    expect(JSON.stringify(row)).toContain('cloudstorage.file.pending_review');
    // El motivo con la puntuación es interno: nunca llega al usuario.
    expect(JSON.stringify(row)).not.toContain('score');
  });

  // E2E F8: el adjunto de chat lo crea la cuenta de servicio; el aviso va a quien mandó el mensaje.
  it('un adjunto de chat avisa al remitente del mensaje, no a la cuenta de servicio', async () => {
    const { handlers, notifications } = setupWithNotifications('Communication', { messageId: 'msg-1', senderId: 'staff-1' });

    await handlers.get('cloudstorage.file.pending_review.v1')!(
      envelopeWithCreatedBy('cloudstorage.file.pending_review.v1', { FileId: 'file-6', CreatedBy: 'communication-service' }),
    );
    await handlers.get('cloudstorage.file.blocked_by_policy.v1')!(
      envelopeWithCreatedBy('cloudstorage.file.blocked_by_policy.v1', { FileId: 'file-6', CreatedBy: 'communication-service' }),
    );

    const recipients = vi.mocked(notifications.createIfMissing).mock.calls.map((call) => call[0]!.userId);
    expect(recipients).toEqual(['staff-1', 'staff-1']);
  });

  it('file.pending_review.v1 NO notifica cuando el flag apunta a Notification', async () => {
    const { handlers, notifications } = setupWithNotifications('Notification');

    await handlers.get('cloudstorage.file.pending_review.v1')!(
      envelopeWithCreatedBy('cloudstorage.file.pending_review.v1', { FileId: 'file-5', CreatedBy: 'uploader-5' }),
    );

    expect(notifications.createIfMissing).not.toHaveBeenCalled();
  });

  it('file.available.v1 tampoco notifica cuando CreatedBy falta', async () => {
    const { handlers, notifications } = setupWithNotifications();

    await handlers.get('cloudstorage.file.available.v1')!(
      envelopeWithCreatedBy('cloudstorage.file.available.v1', { FileId: 'file-3' }),
    );

    expect(notifications.createIfMissing).not.toHaveBeenCalled();
  });
});
