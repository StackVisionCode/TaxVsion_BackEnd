import { describe, expect, it, vi } from 'vitest';
import type { NotificationRepository } from '../../src/application/ports/notification-repository.js';
import {
  applyNotificationsBulkByFilter,
  dismissNotification,
  getNotification,
  getNotificationBySource,
  markAllNotificationsRead,
  markNotificationRead,
} from '../../src/application/use-cases/notification-queries.js';
import { toNotificationDto } from '../../src/application/use-cases/notification-dto.js';
import { Notification, type NotificationSnapshot } from '../../src/domain/notifications/notification.js';
import { expectRejectedCrossTenant } from '../helpers/tenant-isolation.js';

function repository(): NotificationRepository {
  return {
    createIfMissing: vi.fn(),
    findById: vi.fn(),
    markRead: vi.fn().mockResolvedValue(true),
    dismiss: vi.fn().mockResolvedValue(true),
    markAllRead: vi.fn().mockResolvedValue({ markedCount: 3, unreadCount: 1 }),
    listForUser: vi.fn(),
    countUnread: vi.fn().mockResolvedValue(1),
    markUnread: vi.fn().mockResolvedValue(true),
    markManyRead: vi.fn().mockResolvedValue({ markedCount: 0, unreadCount: 0 }),
    markManyUnread: vi.fn().mockResolvedValue({ unmarkedCount: 0, unreadCount: 0 }),
    dismissMany: vi.fn().mockResolvedValue({ dismissedCount: 0, unreadCount: 0 }),
    countForUser: vi.fn().mockResolvedValue(1),
    listSinceForUser: vi.fn().mockResolvedValue([]),
    bulkByFilter: vi.fn().mockResolvedValue({ affectedCount: 5, unreadCount: 2 }),
  };
}

describe('notification mutation use cases', () => {
  it('marks one notification through the directed repository mutation', async () => {
    const notifications = repository();

    const result = await markNotificationRead(
      { tenantId: 'tenant-1', userId: 'user-1', notificationId: 'notification-1' },
      { notifications },
    );

    expect(result.isSuccess).toBe(true);
    expect(notifications.markRead).toHaveBeenCalledWith(
      'tenant-1',
      'user-1',
      'notification-1',
      expect.any(Date),
    );
    expect(notifications.findById).not.toHaveBeenCalled();
  });

  it('dismisses through the directed repository mutation', async () => {
    const notifications = repository();

    const result = await dismissNotification(
      { tenantId: 'tenant-1', userId: 'user-1', notificationId: 'notification-1' },
      { notifications },
    );

    expect(result.isSuccess).toBe(true);
    expect(notifications.dismiss).toHaveBeenCalledWith(
      'tenant-1',
      'user-1',
      'notification-1',
      expect.any(Date),
    );
  });

  it('preserves the explicit read-all cutoff and returns a versioned count', async () => {
    const notifications = repository();
    const cutoffUtc = new Date('2026-10-18T10:00:00.000Z');

    const result = await markAllNotificationsRead(
      { tenantId: 'tenant-1', userId: 'user-1', cutoffUtc },
      { notifications },
    );

    expect(result.isSuccess).toBe(true);
    if (!result.isSuccess) return;
    expect(result.value).toMatchObject({
      cutoffUtc: cutoffUtc.toISOString(),
      markedCount: 3,
      unreadCount: 1,
    });
    expect(Date.parse(result.value.asOfUtc)).not.toBeNaN();
    expect(notifications.markAllRead).toHaveBeenCalledWith({
      tenantId: 'tenant-1',
      userId: 'user-1',
      cutoffUtc,
      readAtUtc: expect.any(Date),
    });
  });
});

function snapshot(overrides: Partial<NotificationSnapshot> = {}): NotificationSnapshot {
  return {
    id: 'notification-1',
    tenantId: 'tenant-1',
    userId: 'user-1',
    kind: 'signature.completed',
    priority: 'High',
    title: 'Document signed',
    body: 'The client signed the engagement letter.',
    metadata: { source: 'signature' },
    category: 'documents',
    actorUserId: null,
    actorDisplayName: null,
    actorType: 'System',
    resourceType: 'signature-request',
    resourceId: 'request-1',
    resourceContext: { customerId: 'customer-1' },
    titleKey: null,
    bodyKey: null,
    params: { documentName: 'Engagement letter' },
    action: null,
    groupKey: null,
    occurredAtUtc: new Date('2026-11-20T10:00:00.000Z'),
    sourceEventId: 'event-1',
    sourceEventType: 'SignatureCompleted',
    correlationId: null,
    readAtUtc: null,
    dismissedAtUtc: null,
    createdAtUtc: new Date('2026-11-20T10:00:01.000Z'),
    ...overrides,
  };
}

// Falso que filtra por oficina y usuario, igual que el repositorio Prisma.
function scopedRepository(rows: readonly NotificationSnapshot[]): NotificationRepository {
  const notifications = repository();
  notifications.findById = vi.fn(async (tenantId: string, id: string, userId: string) => {
    const row = rows.find((r) => r.id === id && r.tenantId === tenantId && r.userId === userId);
    return row ? Notification.rehydrate(row) : null;
  });
  return notifications;
}

describe('getNotification', () => {
  it('returns the notification as the list does', async () => {
    const row = snapshot();
    const notifications = scopedRepository([row]);

    const result = await getNotification(
      { tenantId: 'tenant-1', userId: 'user-1', notificationId: 'notification-1' },
      { notifications },
    );

    expect(result.isSuccess).toBe(true);
    if (!result.isSuccess) return;
    expect(result.value).toEqual(toNotificationDto(row));
    expect(notifications.findById).toHaveBeenCalledWith('tenant-1', 'notification-1', 'user-1');
    expect(notifications.markRead).not.toHaveBeenCalled();
  });

  it('a dismissed notification is not found', async () => {
    const notifications = scopedRepository([
      snapshot({ dismissedAtUtc: new Date('2026-11-21T00:00:00.000Z') }),
    ]);

    const result = await getNotification(
      { tenantId: 'tenant-1', userId: 'user-1', notificationId: 'notification-1' },
      { notifications },
    );

    expect(result).toEqual({
      isSuccess: false,
      error: { code: 'Notification.NotFound', message: 'Notification not found.' },
    });
  });

  it('a notification of another user or tenant is not found', async () => {
    const notifications = scopedRepository([snapshot()]);

    const otherUser = await getNotification(
      { tenantId: 'tenant-1', userId: 'user-2', notificationId: 'notification-1' },
      { notifications },
    );
    const otherTenant = await getNotification(
      { tenantId: 'tenant-2', userId: 'user-1', notificationId: 'notification-1' },
      { notifications },
    );

    expectRejectedCrossTenant(otherUser, ['Notification.NotFound']);
    expectRejectedCrossTenant(otherTenant, ['Notification.NotFound']);
  });
});

describe('getNotificationBySource', () => {
  function bySource(rows: readonly NotificationSnapshot[]): NotificationRepository {
    const notifications = repository();
    notifications.findBySourceEvent = vi.fn(
      async (tenantId: string, sourceEventId: string, userId: string) => {
        const row = rows.find(
          (r) => r.sourceEventId === sourceEventId && r.tenantId === tenantId && r.userId === userId,
        );
        return row ? Notification.rehydrate(row) : null;
      },
    );
    return notifications;
  }

  it("finds the caller's row by source event", async () => {
    const row = snapshot();
    const notifications = bySource([row]);

    const result = await getNotificationBySource(
      { tenantId: 'tenant-1', userId: 'user-1', sourceEventId: 'event-1' },
      { notifications },
    );

    expect(result.isSuccess).toBe(true);
    if (!result.isSuccess) return;
    expect(result.value).toEqual(toNotificationDto(row));
    expect(notifications.markRead).not.toHaveBeenCalled();
  });

  it("returns NotFound for another user's source event", async () => {
    const notifications = bySource([snapshot({ userId: 'user-2' })]);

    const result = await getNotificationBySource(
      { tenantId: 'tenant-1', userId: 'user-1', sourceEventId: 'event-1' },
      { notifications },
    );

    expect(result).toEqual({
      isSuccess: false,
      error: { code: 'Notification.NotFound', message: 'Notification not found.' },
    });
  });

  it('a dismissed row is not found', async () => {
    const notifications = bySource([snapshot({ dismissedAtUtc: new Date('2026-11-21T00:00:00.000Z') })]);

    const result = await getNotificationBySource(
      { tenantId: 'tenant-1', userId: 'user-1', sourceEventId: 'event-1' },
      { notifications },
    );

    expect(result.isSuccess).toBe(false);
  });
});

describe('applyNotificationsBulkByFilter', () => {
  it('keeps a past cutoff and returns the counts', async () => {
    const notifications = repository();
    const upToCreatedAtUtc = new Date('2026-11-20T10:00:00.000Z');

    const result = await applyNotificationsBulkByFilter(
      {
        tenantId: 'tenant-1',
        userId: 'user-1',
        action: 'unread',
        filter: { unreadOnly: false },
        upToCreatedAtUtc,
      },
      { notifications },
    );

    expect(result.isSuccess).toBe(true);
    if (!result.isSuccess) return;
    expect(result.value).toMatchObject({
      affectedCount: 5,
      unreadCount: 2,
      cutoffUtc: upToCreatedAtUtc.toISOString(),
    });
    expect(notifications.bulkByFilter).toHaveBeenCalledWith({
      tenantId: 'tenant-1',
      userId: 'user-1',
      action: 'unread',
      filter: { unreadOnly: false },
      cutoffUtc: upToCreatedAtUtc,
      atUtc: expect.any(Date),
    });
  });

  it('clamps a cutoff from the future to now', async () => {
    const notifications = repository();
    const before = Date.now();

    const result = await applyNotificationsBulkByFilter(
      {
        tenantId: 'tenant-1',
        userId: 'user-1',
        action: 'read',
        filter: {},
        upToCreatedAtUtc: new Date(before + 86_400_000),
      },
      { notifications },
    );

    expect(result.isSuccess).toBe(true);
    if (!result.isSuccess) return;
    const cutoff = Date.parse(result.value.cutoffUtc);
    expect(cutoff).toBeGreaterThanOrEqual(before);
    expect(cutoff).toBeLessThanOrEqual(Date.now());
  });

  it('rejects an invalid cutoff', async () => {
    const notifications = repository();

    const result = await applyNotificationsBulkByFilter(
      {
        tenantId: 'tenant-1',
        userId: 'user-1',
        action: 'read',
        filter: {},
        upToCreatedAtUtc: new Date('nope'),
      },
      { notifications },
    );

    expect(result.isSuccess).toBe(false);
    expect(notifications.bulkByFilter).not.toHaveBeenCalled();
  });
});
