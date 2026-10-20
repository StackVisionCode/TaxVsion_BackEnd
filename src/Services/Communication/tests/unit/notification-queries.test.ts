import { describe, expect, it, vi } from 'vitest';
import type { NotificationRepository } from '../../src/application/ports/notification-repository.js';
import {
  dismissNotification,
  markAllNotificationsRead,
  markNotificationRead,
} from '../../src/application/use-cases/notification-queries.js';

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
