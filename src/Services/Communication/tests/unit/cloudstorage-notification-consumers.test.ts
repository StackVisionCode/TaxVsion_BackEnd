import { describe, expect, it, vi } from 'vitest';
import { bindCloudStorageNotificationConsumers } from '../../src/application/event-handlers/cloudstorage-notification-consumers.js';
import type { NotificationRepository } from '../../src/application/ports/notification-repository.js';
import type { IncomingEnvelope } from '../../src/application/ports/event-consumer.js';
import type { RealtimeEmitter } from '../../src/application/ports/realtime-emitter.js';

/**
 * Test de contrato (regla de la Fase 0 del plan de notificaciones): los payloads de
 * abajo usan los nombres de campo EXACTOS que .NET serializa hoy (PascalCase,
 * copiados literalmente de los records en
 * BuildingBlocks/Messaging/CloudStorageIntegrationEvents/CloudStorageIntegrationEvents.cs
 * — Fase 1B: estos 8 eventos no llevaban ningun actor antes de esta fase).
 */
function setup(inAppSource: 'Communication' | 'Notification' = 'Communication') {
  const handlers = new Map<string, (env: IncomingEnvelope) => Promise<void>>();
  const register = (eventType: string, handler: (env: IncomingEnvelope) => Promise<void>) => {
    handlers.set(eventType, handler);
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
    emitToUser: vi.fn(),
    emitToConversation: vi.fn(),
    emitToCall: vi.fn(),
    emitToMeeting: vi.fn(),
    emitToTenant: vi.fn(),
    emitToTenantStaff: vi.fn(),
    emitToTenantMembers: vi.fn(),
    emitBroadcast: vi.fn(),
  } as unknown as RealtimeEmitter;

  bindCloudStorageNotificationConsumers(register, { notifications, emitter, inAppSource });
  return { handlers, notifications, emitter };
}

function envelope(eventType: string, payload: Record<string, unknown>): IncomingEnvelope {
  return {
    eventId: 'evt-1',
    eventType,
    tenantId: 'tenant-1',
    correlationId: 'corr-1',
    occurredOnUtc: new Date().toISOString(),
    payload,
  };
}

describe('bindCloudStorageNotificationConsumers — contrato de campos con CloudStorage (.NET)', () => {
  it('con inAppSource=Notification no registra los handlers (los crea Notification .NET)', () => {
    const { handlers } = setup('Notification');

    expect(handlers.has('cloudstorage.sharelink.folder_item_added.v1')).toBe(false);
    expect(handlers.has('cloudstorage.file.blocked_by_dmca_takedown.v1')).toBe(false);
    expect(handlers.has('cloudstorage.file.legal_hold_placed.v1')).toBe(false);
  });

  it('sharelink.folder_item_added.v1 crea la notificacion para CreatedByUserId (dueño del archivo, no el creador del link)', async () => {
    const { handlers, notifications } = setup();

    await handlers.get('cloudstorage.sharelink.folder_item_added.v1')!(
      envelope('cloudstorage.sharelink.folder_item_added.v1', {
        ShareLinkId: 'link-1',
        FolderId: 'folder-1',
        FileId: 'file-2',
        AutoCovered: true,
        CreatedByUserId: 'owner-2',
      }),
    );

    expect(notifications.createIfMissing).toHaveBeenCalledTimes(1);
    expect(vi.mocked(notifications.createIfMissing).mock.calls[0]![0]!.userId).toBe('owner-2');
  });

  it('no registra consumers de campana para eventos informativos de bajo valor', () => {
    const { handlers } = setup();

    expect(handlers.has('cloudstorage.file.restored.v1')).toBe(false);
    expect(handlers.has('cloudstorage.sharelink.accessed.v1')).toBe(false);
    expect(handlers.has('cloudstorage.sharelink.expired.v1')).toBe(false);
  });

  it('file.blocked_by_dmca_takedown.v1 crea la notificacion para CreatedBy', async () => {
    const { handlers, notifications } = setup();

    await handlers.get('cloudstorage.file.blocked_by_dmca_takedown.v1')!(
      envelope('cloudstorage.file.blocked_by_dmca_takedown.v1', {
        FileId: 'file-5',
        DmcaNoticeId: 'notice-1',
        CreatedBy: 'owner-5',
      }),
    );

    expect(notifications.createIfMissing).toHaveBeenCalledTimes(1);
    expect(vi.mocked(notifications.createIfMissing).mock.calls[0]![0]!.userId).toBe('owner-5');
  });

  it('file.reinstated_from_takedown.v1 crea la notificacion para CreatedBy', async () => {
    const { handlers, notifications } = setup();

    await handlers.get('cloudstorage.file.reinstated_from_takedown.v1')!(
      envelope('cloudstorage.file.reinstated_from_takedown.v1', {
        FileId: 'file-6',
        DmcaNoticeId: 'notice-2',
        CreatedBy: 'owner-6',
      }),
    );

    expect(notifications.createIfMissing).toHaveBeenCalledTimes(1);
    expect(vi.mocked(notifications.createIfMissing).mock.calls[0]![0]!.userId).toBe('owner-6');
  });

  it('file.legal_hold_placed.v1 crea la notificacion para CreatedBy', async () => {
    const { handlers, notifications } = setup();

    await handlers.get('cloudstorage.file.legal_hold_placed.v1')!(
      envelope('cloudstorage.file.legal_hold_placed.v1', {
        FileId: 'file-7',
        CreatedBy: 'owner-7',
        ActorId: 'legal-actor-1',
        Reason: 'litigation-2026-001',
      }),
    );

    expect(notifications.createIfMissing).toHaveBeenCalledTimes(1);
    expect(vi.mocked(notifications.createIfMissing).mock.calls[0]![0]!.userId).toBe('owner-7');
  });

  it('file.legal_hold_lifted.v1 crea la notificacion para CreatedBy', async () => {
    const { handlers, notifications } = setup();

    await handlers.get('cloudstorage.file.legal_hold_lifted.v1')!(
      envelope('cloudstorage.file.legal_hold_lifted.v1', {
        FileId: 'file-8',
        CreatedBy: 'owner-8',
        ActorId: 'legal-actor-2',
      }),
    );

    expect(notifications.createIfMissing).toHaveBeenCalledTimes(1);
    expect(vi.mocked(notifications.createIfMissing).mock.calls[0]![0]!.userId).toBe('owner-8');
  });

  it('no crea ninguna notificacion si CreatedBy/CreatedByUserId falta', async () => {
    const { handlers, notifications } = setup();

    await handlers.get('cloudstorage.file.blocked_by_dmca_takedown.v1')!(
      envelope('cloudstorage.file.blocked_by_dmca_takedown.v1', { FileId: 'file-9' }),
    );

    expect(notifications.createIfMissing).not.toHaveBeenCalled();
  });
});
