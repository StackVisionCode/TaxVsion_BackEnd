import { afterEach, describe, expect, it, vi } from 'vitest';
import type { PrismaClient } from '@prisma/client';
import {
  reconcilePortalAccountsOnce,
  startPortalAccountReconciliationScheduler,
} from '../../src/infrastructure/schedulers/portal-account-reconciliation-scheduler.js';
import {
  HttpPortalAccountsReconciliationClient,
  type PortalAccountReconciliationPage,
  type PortalAccountReconciliationRow,
} from '../../src/infrastructure/auth/http-portal-accounts-reconciliation-client.js';
import { PrismaCustomerPortalAccountRepository } from '../../src/infrastructure/persistence/prisma-customer-portal-account-repository.js';
import type { CustomerPortalAccountReconcileOutcome } from '../../src/application/ports/customer-portal-account-repository.js';
import type { ServiceTokenClient } from '../../src/infrastructure/auth/service-token-client.js';
import type { RedisDistributedLock } from '../../src/infrastructure/redis/redis-distributed-lock.js';

/**
 * CustomerPortalAccount se repara contra Auth (`internal/users/portal-accounts/reconciliation`): activa
 * → upsert, inactiva → baja solo la fila de ese usuario, y nunca se borra nada.
 */
function row(overrides: Partial<PortalAccountReconciliationRow> = {}): PortalAccountReconciliationRow {
  return { tenantId: 'tenant-1', customerId: 'cust-1', userId: 'user-1', isActive: true, ...overrides };
}

function pagedClient(pages: (PortalAccountReconciliationPage | null)[]) {
  return { listPage: vi.fn(async (page: number) => pages[page - 1] ?? null) };
}

function writer(outcome: (r: PortalAccountReconciliationRow) => CustomerPortalAccountReconcileOutcome) {
  return { applyReconciled: vi.fn(async (r: PortalAccountReconciliationRow) => outcome(r)) };
}

afterEach(() => vi.unstubAllGlobals());

describe('reconcilePortalAccountsOnce', () => {
  it('recorre todas las paginas y aplica cada cuenta, contando resultados', async () => {
    const client = pagedClient([
      { items: [row(), row({ customerId: 'cust-2', userId: 'user-2', isActive: false })], hasMore: true },
      { items: [row({ customerId: 'cust-3', userId: 'user-3', isActive: false })], hasMore: false },
    ]);
    const portalAccounts = writer((r) =>
      r.isActive ? 'upserted' : r.userId === 'user-2' ? 'deactivated' : 'unchanged',
    );

    const outcome = await reconcilePortalAccountsOnce({ client, portalAccounts });

    expect(outcome).toEqual({ upserted: 1, deactivated: 1, unchanged: 1, skipped: 0, completed: true });
    expect(client.listPage.mock.calls.map((c) => c[0])).toEqual([1, 2]);
    expect(portalAccounts.applyReconciled).toHaveBeenNthCalledWith(1, row());
  });

  it('salta filas incompletas', async () => {
    const client = pagedClient([{ items: [row({ customerId: '' }), row({ userId: '' })], hasMore: false }]);
    const portalAccounts = writer(() => 'upserted');

    const outcome = await reconcilePortalAccountsOnce({ client, portalAccounts });

    expect(outcome.skipped).toBe(2);
    expect(portalAccounts.applyReconciled).not.toHaveBeenCalled();
  });

  it('corta la pasada si Auth no responde', async () => {
    const client = pagedClient([{ items: [row()], hasMore: true }, null]);

    const outcome = await reconcilePortalAccountsOnce({ client, portalAccounts: writer(() => 'upserted') });

    expect(outcome).toMatchObject({ upserted: 1, completed: false });
  });
});

describe('startPortalAccountReconciliationScheduler', () => {
  it('con el lock tomado por otro pod no llama a Auth', async () => {
    const client = pagedClient([{ items: [row()], hasMore: false }]);
    const lock = { withLock: vi.fn().mockResolvedValue(undefined) };
    const scheduler = startPortalAccountReconciliationScheduler(
      { enabled: true, intervalHours: 12 },
      { client, portalAccounts: writer(() => 'upserted'), lock },
    );
    await scheduler.tick();
    scheduler.stop();

    expect(lock.withLock).toHaveBeenCalledWith(
      'comm:lock:portal-account-reconciliation-scheduler',
      24 * 3_600_000,
      expect.any(Function),
    );
    expect(client.listPage).not.toHaveBeenCalled();
  });

  it('con el lock corre la pasada (al arrancar y en cada tick)', async () => {
    const portalAccounts = writer(() => 'upserted');
    const lock = {
      withLock: vi.fn(async (_k: string, _t: number, fn: () => Promise<unknown>) => fn()),
    } as unknown as Pick<RedisDistributedLock, 'withLock'>;
    const scheduler = startPortalAccountReconciliationScheduler(
      { enabled: true, intervalHours: 12 },
      { client: pagedClient([{ items: [row()], hasMore: false }]), portalAccounts, lock },
    );
    await scheduler.tick();
    scheduler.stop();

    expect(lock.withLock).toHaveBeenCalledTimes(2);
    expect(portalAccounts.applyReconciled).toHaveBeenCalled();
  });

  it('apagado por config no programa nada', async () => {
    const lock = { withLock: vi.fn() };
    const scheduler = startPortalAccountReconciliationScheduler(
      { enabled: false, intervalHours: 12 },
      { client: pagedClient([]), portalAccounts: writer(() => 'upserted'), lock },
    );
    await scheduler.tick();
    expect(lock.withLock).not.toHaveBeenCalled();
  });
});

describe('PrismaCustomerPortalAccountRepository.applyReconciled', () => {
  function prismaFake(updatedCount: number) {
    const customerPortalAccount = {
      upsert: vi.fn(),
      updateMany: vi.fn().mockResolvedValue({ count: updatedCount }),
    };
    return { prisma: { customerPortalAccount } as unknown as PrismaClient, customerPortalAccount };
  }

  it('cuenta activa → upsert (gana y reemplaza el UserId del cliente)', async () => {
    const { prisma, customerPortalAccount } = prismaFake(0);

    const outcome = await new PrismaCustomerPortalAccountRepository(prisma).applyReconciled(row());

    expect(outcome).toBe('upserted');
    expect(customerPortalAccount.upsert).toHaveBeenCalledWith(
      expect.objectContaining({
        where: { CustomerId: 'cust-1' },
        update: { TenantId: 'tenant-1', UserId: 'user-1', IsActive: true },
      }),
    );
    expect(customerPortalAccount.updateMany).not.toHaveBeenCalled();
  });

  it('cuenta inactiva → baja solo la fila de ese usuario, sin crear ni pisar otra', async () => {
    const { prisma, customerPortalAccount } = prismaFake(1);

    const outcome = await new PrismaCustomerPortalAccountRepository(prisma).applyReconciled(
      row({ isActive: false }),
    );

    expect(outcome).toBe('deactivated');
    expect(customerPortalAccount.updateMany).toHaveBeenCalledWith({
      where: { CustomerId: 'cust-1', UserId: 'user-1', IsActive: true },
      data: { IsActive: false },
    });
    expect(customerPortalAccount.upsert).not.toHaveBeenCalled();
  });

  it('cuenta inactiva de un usuario viejo (la fila ya es de la cuenta nueva) → sin cambios', async () => {
    const { prisma } = prismaFake(0);

    const outcome = await new PrismaCustomerPortalAccountRepository(prisma).applyReconciled(
      row({ userId: 'old-user', isActive: false }),
    );

    expect(outcome).toBe('unchanged');
  });
});

describe('HttpPortalAccountsReconciliationClient', () => {
  const tokens = { getToken: vi.fn().mockResolvedValue('svc-token') } as unknown as ServiceTokenClient;

  it('pide el endpoint interno de Auth y mapea camel o Pascal', async () => {
    const fetchMock = vi.fn(
      async () =>
        new Response(
          JSON.stringify({
            items: [
              { tenantId: 't', customerId: 'c-1', userId: 'u-1', isActive: true },
              { TenantId: 't', CustomerId: 'c-2', UserId: 'u-2', IsActive: false },
            ],
            page: 1,
            size: 2,
            totalCount: 2,
          }),
        ),
    );
    vi.stubGlobal('fetch', fetchMock);

    const page = await new HttpPortalAccountsReconciliationClient(tokens).listPage(1, 2);

    expect(String((fetchMock.mock.calls[0] as unknown[])[0])).toMatch(
      /\/internal\/users\/portal-accounts\/reconciliation\?page=1&size=2$/,
    );
    expect(page).toEqual({
      hasMore: false,
      items: [
        { tenantId: 't', customerId: 'c-1', userId: 'u-1', isActive: true },
        { tenantId: 't', customerId: 'c-2', userId: 'u-2', isActive: false },
      ],
    });
  });

  it('un status no exitoso devuelve null (fail-soft)', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn(async () => new Response('{}', { status: 403 })),
    );
    expect(await new HttpPortalAccountsReconciliationClient(tokens).listPage(1, 200)).toBeNull();
  });
});
