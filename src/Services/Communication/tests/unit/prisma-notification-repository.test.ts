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

describe('PrismaNotificationRepository bulkByFilter', () => {
  function fakeRows(total: number) {
    let pending = Array.from({ length: total }, (_, index) => `id-${index}`);
    const findMany = vi.fn(async (args: { take: number }) =>
      pending.slice(0, args.take).map((Id) => ({ Id })),
    );
    const updateMany = vi.fn(async (args: { where: { Id: { in: string[] } } }) => {
      const ids = new Set(args.where.Id.in);
      const before = pending.length;
      pending = pending.filter((id) => !ids.has(id));
      return { count: before - pending.length };
    });
    const count = vi.fn(async () => 0);
    const repo = new PrismaNotificationRepository({
      notificationEntry: { findMany, updateMany, count },
    } as never);
    return { repo, findMany, updateMany, count };
  }

  it('bulkByFilter processes in batches', async () => {
    const { repo, findMany, updateMany } = fakeRows(2500);
    const cutoffUtc = new Date('2026-11-20T10:00:00.000Z');

    const result = await repo.bulkByFilter({
      tenantId: 'tenant-1',
      userId: 'user-1',
      action: 'read',
      filter: {},
      cutoffUtc,
      atUtc: new Date('2026-11-20T10:05:00.000Z'),
    });

    expect(result.affectedCount).toBe(2500);
    expect(findMany).toHaveBeenCalledTimes(3);
    expect(updateMany).toHaveBeenCalledTimes(3);
    expect(updateMany.mock.calls.map(([args]) => args.where.Id.in.length)).toEqual([1000, 1000, 500]);
    for (const [args] of findMany.mock.calls) expect(args.take).toBe(1000);
  });

  it('bulkByFilter scopes every batch by tenant, user, cutoff and pending state', async () => {
    const { repo, findMany, updateMany } = fakeRows(3);
    const cutoffUtc = new Date('2026-11-20T10:00:00.000Z');
    const atUtc = new Date('2026-11-20T10:05:00.000Z');

    await repo.bulkByFilter({
      tenantId: 'tenant-1',
      userId: 'user-1',
      action: 'dismissed',
      filter: { category: 'Billing' },
      cutoffUtc,
      atUtc,
    });

    const scope = {
      TenantId: 'tenant-1',
      UserId: 'user-1',
      DismissedAtUtc: null,
      Category: 'Billing',
      CreatedAtUtc: { lte: cutoffUtc },
    };
    expect(findMany.mock.calls[0]?.[0]).toMatchObject({ where: scope });
    expect(updateMany.mock.calls[0]?.[0]).toMatchObject({
      where: { ...scope, Id: { in: ['id-0', 'id-1', 'id-2'] } },
      data: { DismissedAtUtc: atUtc },
    });
  });
});

describe('PrismaNotificationRepository listSinceForUser', () => {
  type Row = Record<string, unknown>;
  type Where = Record<string, unknown>;

  // Evalúa en memoria el subconjunto de filtros de Prisma que usa la consulta.
  function matches(row: Row, where: Where): boolean {
    return Object.entries(where).every(([key, cond]) => {
      if (key === 'OR') return (cond as Where[]).some((w) => matches(row, w));
      if (key === 'AND') return (cond as Where[]).every((w) => matches(row, w));
      const value = row[key];
      if (cond === null) return value === null;
      if (cond instanceof Date) return value instanceof Date && value.getTime() === cond.getTime();
      if (typeof cond === 'object') {
        return Object.entries(cond as Record<string, unknown>).every(([op, x]) => {
          const a = value instanceof Date ? value.getTime() : (value as string);
          const b = x instanceof Date ? x.getTime() : (x as string);
          if (op === 'gt') return a > b;
          if (op === 'equals') return a === b;
          throw new Error(`Unsupported operator ${op}`);
        });
      }
      return value === cond;
    });
  }

  function fakeTable(rows: Row[]) {
    const findMany = vi.fn(
      async (args: { where: Where; orderBy: Record<string, 'asc' | 'desc'>[]; take: number }) =>
        rows
          .filter((row) => matches(row, args.where))
          .sort((x, y) => {
            for (const order of args.orderBy) {
              for (const [key, dir] of Object.entries(order)) {
                const a = x[key] instanceof Date ? (x[key] as Date).getTime() : (x[key] as string);
                const b = y[key] instanceof Date ? (y[key] as Date).getTime() : (y[key] as string);
                if (a !== b) return (a < b ? -1 : 1) * (dir === 'asc' ? 1 : -1);
              }
            }
            return 0;
          })
          .slice(0, args.take),
    );
    return new PrismaNotificationRepository({ notificationEntry: { findMany } } as never);
  }

  function row(id: string, createdAtUtc: Date): Row {
    return {
      Id: id,
      TenantId: 'tenant-1',
      UserId: 'user-1',
      Kind: 'billing.invoice_issued',
      Priority: 'Normal',
      Title: 'Invoice issued',
      Body: 'A new invoice is ready.',
      MetadataJson: '{}',
      Category: 'Billing',
      ActorUserId: null,
      ActorDisplayName: null,
      ActorType: null,
      ResourceType: null,
      ResourceId: null,
      ResourceContextJson: null,
      TitleKey: null,
      BodyKey: null,
      ParamsJson: null,
      Action: null,
      GroupKey: null,
      OccurredAtUtc: null,
      SourceEventId: id,
      SourceEventType: 'InvoiceIssued',
      CorrelationId: null,
      ReadAtUtc: null,
      DismissedAtUtc: null,
      CreatedAtUtc: createdAtUtc,
    };
  }

  it('two rows in the same millisecond are both returned across pages', async () => {
    const sameMs = new Date('2026-11-22T10:00:00.123Z');
    const repo = fakeTable([
      row('00000000-0000-0000-0000-00000000000b', sameMs),
      row('00000000-0000-0000-0000-00000000000a', sameMs),
    ]);
    const scope = { tenantId: 'tenant-1', userId: 'user-1', limit: 1 };

    const first = await repo.listSinceForUser({ ...scope, sinceUtc: new Date('2026-11-22T09:00:00.000Z') });
    const [last] = first;
    if (!last) throw new Error('first page is empty');
    const second = await repo.listSinceForUser({ ...scope, sinceUtc: last.createdAtUtc, sinceId: last.id });

    expect([...first, ...second].map((n) => n.id)).toEqual([
      '00000000-0000-0000-0000-00000000000a',
      '00000000-0000-0000-0000-00000000000b',
    ]);
  });

  it('without sinceId keeps the millisecond cursor', async () => {
    const at = new Date('2026-11-22T10:00:00.123Z');
    const repo = fakeTable([row('00000000-0000-0000-0000-00000000000a', at)]);

    const result = await repo.listSinceForUser({
      tenantId: 'tenant-1',
      userId: 'user-1',
      limit: 10,
      sinceUtc: at,
    });

    expect(result).toEqual([]);
  });
});
