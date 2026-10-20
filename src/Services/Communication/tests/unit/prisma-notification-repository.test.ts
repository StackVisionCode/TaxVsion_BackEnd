import { describe, expect, it, vi } from 'vitest';
import { PrismaNotificationRepository } from '../../src/infrastructure/persistence/prisma-notification-repository.js';

describe('PrismaNotificationRepository directed mutations', () => {
  it('markRead updates only ReadAtUtc within tenant and user scope', async () => {
    const updateMany = vi.fn().mockResolvedValue({ count: 1 });
    const repo = new PrismaNotificationRepository({ notificationEntry: { updateMany } } as never);
    const at = new Date('2026-10-18T10:00:00.000Z');

    await repo.markRead('tenant-1', 'user-1', 'notification-1', at);

    expect(updateMany).toHaveBeenCalledWith({
      where: {
        Id: 'notification-1',
        TenantId: 'tenant-1',
        UserId: 'user-1',
        ReadAtUtc: null,
      },
      data: { ReadAtUtc: at },
    });
  });

  it('dismiss updates only DismissedAtUtc within tenant and user scope', async () => {
    const updateMany = vi.fn().mockResolvedValue({ count: 1 });
    const repo = new PrismaNotificationRepository({ notificationEntry: { updateMany } } as never);
    const at = new Date('2026-10-18T10:00:00.000Z');

    await repo.dismiss('tenant-1', 'user-1', 'notification-1', at);

    expect(updateMany).toHaveBeenCalledWith({
      where: {
        Id: 'notification-1',
        TenantId: 'tenant-1',
        UserId: 'user-1',
        DismissedAtUtc: null,
      },
      data: { DismissedAtUtc: at },
    });
  });
});
