import { afterAll, describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { PrismaClient } from '@prisma/client';
import { PrismaNotificationRepository } from '../../src/infrastructure/persistence/prisma-notification-repository.js';
import { listNotifications } from '../../src/application/use-cases/notification-queries.js';

/**
 * Filtro por categoría, búsqueda y acción masiva por filtro con SQL real.
 * Oficinas y usuarios aleatorios; se borran solo estas filas al final.
 */
const prisma = new PrismaClient();
const notifications = new PrismaNotificationRepository(prisma);
const tenants: string[] = [];

interface Row {
  title?: string;
  body?: string;
  category?: string | null;
  actor?: string | null;
  params?: Record<string, string>;
  createdAtUtc?: Date;
  readAtUtc?: Date | null;
  dismissedAtUtc?: Date | null;
  userId?: string;
}

function scope() {
  const tenantId = randomUUID();
  tenants.push(tenantId);
  return { tenantId, userId: randomUUID() };
}

async function seed(tenantId: string, userId: string, rows: readonly Row[]): Promise<string[]> {
  const data = rows.map((row, index) => ({
    Id: randomUUID(),
    TenantId: tenantId,
    UserId: row.userId ?? userId,
    Kind: 'billing.invoice_issued',
    Priority: 'Normal',
    Title: row.title ?? 'Invoice issued',
    Body: row.body ?? 'A new invoice is ready.',
    MetadataJson: '{}',
    Category: row.category === undefined ? 'Billing' : row.category,
    ActorDisplayName: row.actor ?? null,
    ParamsJson: JSON.stringify(row.params ?? {}),
    SourceEventId: randomUUID(),
    SourceEventType: 'InvoiceIssued',
    ReadAtUtc: row.readAtUtc ?? null,
    DismissedAtUtc: row.dismissedAtUtc ?? null,
    CreatedAtUtc: row.createdAtUtc ?? new Date(Date.UTC(2026, 10, 1, 0, 0, index)),
  }));
  for (let start = 0; start < data.length; start += 500) {
    await prisma.notificationEntry.createMany({ data: data.slice(start, start + 500) });
  }
  return data.map((row) => row.Id);
}

async function list(tenantId: string, userId: string, filter: { category?: 'Billing'; q?: string } = {}) {
  const result = await listNotifications(
    { tenantId, userId, page: 1, size: 100, ...filter },
    { notifications },
  );
  if (!result.isSuccess) throw new Error(result.error.message);
  return result.value;
}

afterAll(async () => {
  await prisma.notificationEntry.deleteMany({ where: { TenantId: { in: tenants } } });
  await prisma.$disconnect();
});

describe('notification list filters (SQL Server)', () => {
  it('filters by category', async () => {
    const { tenantId, userId } = scope();
    await seed(tenantId, userId, [{ category: 'Billing' }, { category: 'Work' }, { category: null }]);

    const billing = await list(tenantId, userId, { category: 'Billing' });

    expect(billing.items.map((item) => item.category)).toEqual(['Billing']);
  });

  it('searches title, body, actor and params', async () => {
    const { tenantId, userId } = scope();
    const [byTitle, byBody, byActor, byParams] = await seed(tenantId, userId, [
      { title: 'Payment from SMITH received' },
      { body: 'Smith uploaded a document.' },
      { actor: 'Jane Smith' },
      { params: { customerName: 'Anna Smithson' } },
      { title: 'Unrelated', body: 'Nothing here.' },
    ]);

    const found = await list(tenantId, userId, { q: 'smith' });

    expect(found.items.map((item) => item.id).sort()).toEqual([byTitle, byBody, byActor, byParams].sort());
  });

  it('total counts only the filtered rows', async () => {
    const { tenantId, userId } = scope();
    await seed(tenantId, userId, [
      { category: 'Billing', title: 'Invoice INV-2026-1' },
      { category: 'Billing', title: 'Invoice INV-2026-2', readAtUtc: new Date() },
      { category: 'Billing', title: 'Receipt' },
      { category: 'Work', title: 'Invoice INV-2026-3' },
      { category: 'Billing', title: 'Invoice INV-2026-4', dismissedAtUtc: new Date() },
    ]);

    const filtered = await list(tenantId, userId, { category: 'Billing', q: 'INV-2026' });

    expect(filtered.total).toBe(2);
    expect(filtered.items).toHaveLength(2);
    // La campanita sigue contando todo el buzón.
    expect(filtered.unreadCount).toBe(3);
  });

  it('treats % and _ as literal characters', async () => {
    const { tenantId, userId } = scope();
    const [percent, underscore] = await seed(tenantId, userId, [
      { title: 'Storage at 100% capacity' },
      { title: 'File tax_return uploaded' },
      { title: 'Storage at 100 GB' },
      { title: 'File taxXreturn uploaded' },
      { title: 'Bracket [x] test' },
    ]);

    expect((await list(tenantId, userId, { q: '0%' })).items.map((item) => item.id)).toEqual([percent]);
    expect((await list(tenantId, userId, { q: 'x_r' })).items.map((item) => item.id)).toEqual([underscore]);
    expect((await list(tenantId, userId, { q: '[x]' })).items).toHaveLength(1);
  });

  it('breaks ties by id so pages are stable', async () => {
    const { tenantId, userId } = scope();
    const sameInstant = new Date(Date.UTC(2026, 10, 2));
    const ids = await seed(
      tenantId,
      userId,
      Array.from({ length: 6 }, () => ({ createdAtUtc: sameInstant })),
    );

    const first = await listNotifications({ tenantId, userId, page: 1, size: 3 }, { notifications });
    const second = await listNotifications({ tenantId, userId, page: 2, size: 3 }, { notifications });

    if (!first.isSuccess || !second.isSuccess) throw new Error('list failed');
    const paged = [...first.value.items, ...second.value.items].map((item) => item.id);
    expect(new Set(paged).size).toBe(6);
    expect(paged.sort()).toEqual([...ids].sort());
  });
});

describe('notification bulk by filter (SQL Server)', () => {
  const cutoffUtc = new Date(Date.UTC(2026, 10, 10));
  const before = (minutes: number) => new Date(cutoffUtc.getTime() - minutes * 60_000);
  const after = (minutes: number) => new Date(cutoffUtc.getTime() + minutes * 60_000);

  async function apply(
    tenantId: string,
    userId: string,
    action: 'read' | 'unread' | 'dismissed',
    filter: { unreadOnly?: boolean; category?: 'Billing' | 'Work'; q?: string } = {},
  ) {
    return notifications.bulkByFilter({ tenantId, userId, action, filter, cutoffUtc, atUtc: new Date() });
  }

  it('bulkByFilter marks only rows up to the cutoff', async () => {
    const { tenantId, userId } = scope();
    await seed(tenantId, userId, [
      ...[1, 2, 3, 4, 5].map((m) => ({ createdAtUtc: before(m) })),
      ...[1, 2].map((m) => ({ createdAtUtc: after(m) })),
    ]);

    const result = await apply(tenantId, userId, 'read');

    expect(result).toEqual({ affectedCount: 5, unreadCount: 2 });
    expect(await prisma.notificationEntry.count({ where: { TenantId: tenantId, ReadAtUtc: null } })).toBe(2);
  });

  it('bulkByFilter respects category and search', async () => {
    const { tenantId, userId } = scope();
    const [match] = await seed(tenantId, userId, [
      { category: 'Billing', title: 'Invoice for Smith', createdAtUtc: before(1) },
      { category: 'Billing', title: 'Invoice for Jones', createdAtUtc: before(2) },
      { category: 'Work', title: 'Task for Smith', createdAtUtc: before(3) },
    ]);

    const result = await apply(tenantId, userId, 'dismissed', { category: 'Billing', q: 'Smith' });

    expect(result.affectedCount).toBe(1);
    const dismissed = await prisma.notificationEntry.findMany({
      where: { TenantId: tenantId, DismissedAtUtc: { not: null } },
      select: { Id: true },
    });
    expect(dismissed.map((row) => row.Id)).toEqual([match]);
  });

  it('bulkByFilter ignores dismissed rows', async () => {
    const { tenantId, userId } = scope();
    const dismissedAtUtc = before(30);
    const [dismissed = ''] = await seed(tenantId, userId, [
      { createdAtUtc: before(1), readAtUtc: before(1), dismissedAtUtc },
      { createdAtUtc: before(2), readAtUtc: before(2) },
    ]);

    const result = await apply(tenantId, userId, 'unread');

    expect(result).toEqual({ affectedCount: 1, unreadCount: 1 });
    const row = await prisma.notificationEntry.findUnique({ where: { Id: dismissed } });
    expect(row?.ReadAtUtc).not.toBeNull();
    expect(row?.DismissedAtUtc).toEqual(dismissedAtUtc);
  });

  it("bulkByFilter never touches another user's rows", async () => {
    const { tenantId, userId } = scope();
    const other = scope();
    await seed(tenantId, userId, [{ createdAtUtc: before(1) }]);
    await seed(tenantId, userId, [{ createdAtUtc: before(1), userId: randomUUID() }]);
    await seed(other.tenantId, userId, [{ createdAtUtc: before(1) }]);

    const result = await apply(tenantId, userId, 'read');

    expect(result.affectedCount).toBe(1);
    expect(await prisma.notificationEntry.count({ where: { TenantId: tenantId, ReadAtUtc: null } })).toBe(1);
    expect(
      await prisma.notificationEntry.count({ where: { TenantId: other.tenantId, ReadAtUtc: null } }),
    ).toBe(1);
  });

  it('bulkByFilter processes 2 500 rows in batches', async () => {
    const { tenantId, userId } = scope();
    await seed(
      tenantId,
      userId,
      Array.from({ length: 2500 }, (_, index) => ({ createdAtUtc: before(index + 1) })),
    );

    const result = await apply(tenantId, userId, 'read');

    expect(result).toEqual({ affectedCount: 2500, unreadCount: 0 });
  });
});
