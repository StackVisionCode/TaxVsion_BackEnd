import { describe, expect, it, vi } from 'vitest';
import { purgeNotificationEntries } from '../../src/infrastructure/schedulers/purge-scheduler.js';

describe('notification retention', () => {
  it('purges old read and unread rows in bounded batches', async () => {
    const findMany = vi
      .fn()
      .mockResolvedValueOnce([{ Id: 'n1' }, { Id: 'n2' }])
      .mockResolvedValueOnce([{ Id: 'n3' }]);
    const deleteMany = vi.fn().mockResolvedValueOnce({ count: 2 }).mockResolvedValueOnce({ count: 1 });
    const prisma = { notificationEntry: { findMany, deleteMany } } as never;
    const nowUtc = new Date('2026-10-18T12:00:00.000Z');

    const total = await purgeNotificationEntries(prisma, {
      nowUtc,
      readRetentionDays: 90,
      unreadRetentionDays: 180,
      batchSize: 2,
    });

    expect(total).toBe(3);
    expect(findMany).toHaveBeenCalledTimes(2);
    expect(findMany.mock.calls[0]![0]).toMatchObject({
      where: {
        OR: [
          { ReadAtUtc: { not: null, lt: new Date('2026-07-20T12:00:00.000Z') } },
          { ReadAtUtc: null, CreatedAtUtc: { lt: new Date('2026-04-21T12:00:00.000Z') } },
        ],
      },
      take: 2,
    });
    expect(deleteMany).toHaveBeenNthCalledWith(1, { where: { Id: { in: ['n1', 'n2'] } } });
    expect(deleteMany).toHaveBeenNthCalledWith(2, { where: { Id: { in: ['n3'] } } });
  });
});
