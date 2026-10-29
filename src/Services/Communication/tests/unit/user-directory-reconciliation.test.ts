import { afterEach, describe, expect, it, vi } from 'vitest';
import {
  reconcileUserDirectoryOnce,
  startUserDirectoryReconciliationScheduler,
} from '../../src/infrastructure/schedulers/user-directory-reconciliation-scheduler.js';
import {
  HttpUserDirectoryReconciliationClient,
  type UserDirectoryReconciliationPage,
  type UserDirectoryReconciliationRow,
} from '../../src/infrastructure/auth/http-user-directory-reconciliation-client.js';
import { HttpCustomerReconciliationClient } from '../../src/infrastructure/customer/http-customer-reconciliation-client.js';
import { startCustomerReconciliationScheduler } from '../../src/infrastructure/schedulers/customer-reconciliation-scheduler.js';
import type { ServiceTokenClient } from '../../src/infrastructure/auth/service-token-client.js';
import type { CustomerDirectoryRepository } from '../../src/application/ports/customer-directory-repository.js';
import type { RedisDistributedLock } from '../../src/infrastructure/redis/redis-distributed-lock.js';

/**
 * F5 avatares — UserDirectoryEntry ahora se repara contra Auth (antes no tenia reconciliacion) y la
 * del directorio de clientes trae la foto.
 */
function row(overrides: Partial<UserDirectoryReconciliationRow> = {}): UserDirectoryReconciliationRow {
  return {
    tenantId: 'tenant-1',
    userId: 'user-1',
    actorType: 'TenantEmployee',
    isActive: true,
    displayName: 'Ana Lopez',
    email: 'ana@x.test',
    avatarFileId: 'f-1',
    ...overrides,
  };
}

function pagedClient(pages: (UserDirectoryReconciliationPage | null)[]) {
  return { listPage: vi.fn(async (page: number) => pages[page - 1] ?? null) };
}

const runLock = { withLock: vi.fn(async (_k: string, _t: number, fn: () => Promise<unknown>) => fn()) };

afterEach(() => {
  vi.unstubAllGlobals();
  runLock.withLock.mockClear();
});

describe('reconcileUserDirectoryOnce', () => {
  it('recorre todas las paginas y hace upsert con nombre, estado, actorType y foto', async () => {
    const client = pagedClient([
      {
        items: [
          row(),
          row({ userId: 'user-2', avatarFileId: null, isActive: false, actorType: 'CustomerPortal' }),
        ],
        hasMore: true,
      },
      { items: [row({ userId: 'user-3' })], hasMore: false },
    ]);
    const userDirectory = { upsert: vi.fn() };

    const outcome = await reconcileUserDirectoryOnce({ client, userDirectory });

    expect(outcome).toEqual({ upserted: 3, skipped: 0, completed: true });
    expect(client.listPage.mock.calls.map((c) => c[0])).toEqual([1, 2]);
    expect(userDirectory.upsert).toHaveBeenNthCalledWith(1, {
      userId: 'user-1',
      tenantId: 'tenant-1',
      displayName: 'Ana Lopez',
      email: 'ana@x.test',
      isActive: true,
      avatarFileId: 'f-1',
      actorType: 'TenantEmployee',
    });
    // null de Auth limpia la foto guardada.
    expect(userDirectory.upsert.mock.calls[1]![0]).toMatchObject({
      avatarFileId: null,
      isActive: false,
      actorType: 'CustomerPortal',
    });
  });

  it('salta filas sin email; sin nombre usa el email; recorta a los topes de columna', async () => {
    const longName = 'x'.repeat(300);
    const client = pagedClient([
      {
        items: [
          row({ email: '' }),
          row({ userId: 'user-2', displayName: '  ' }),
          row({ userId: 'user-3', displayName: longName }),
        ],
        hasMore: false,
      },
    ]);
    const userDirectory = { upsert: vi.fn() };

    const outcome = await reconcileUserDirectoryOnce({ client, userDirectory });

    expect(outcome).toMatchObject({ upserted: 2, skipped: 1 });
    expect(userDirectory.upsert.mock.calls[0]![0].displayName).toBe('ana@x.test');
    expect(userDirectory.upsert.mock.calls[1]![0].displayName).toHaveLength(200);
  });

  it('corta la pasada si Auth no responde', async () => {
    const client = pagedClient([{ items: [row()], hasMore: true }, null]);
    const userDirectory = { upsert: vi.fn() };

    const outcome = await reconcileUserDirectoryOnce({ client, userDirectory });

    expect(outcome).toEqual({ upserted: 1, skipped: 0, completed: false });
  });
});

describe('startUserDirectoryReconciliationScheduler', () => {
  it('con el lock tomado por otro pod no llama a Auth', async () => {
    const client = pagedClient([{ items: [row()], hasMore: false }]);
    const lock = { withLock: vi.fn().mockResolvedValue(undefined) };
    const scheduler = startUserDirectoryReconciliationScheduler(
      { enabled: true, intervalHours: 12 },
      { client, userDirectory: { upsert: vi.fn() }, lock },
    );
    await scheduler.tick();
    scheduler.stop();

    expect(lock.withLock).toHaveBeenCalledWith(
      'comm:lock:user-directory-reconciliation-scheduler',
      24 * 3_600_000,
      expect.any(Function),
    );
    expect(client.listPage).not.toHaveBeenCalled();
  });

  it('con el lock corre la pasada (tambien al arrancar)', async () => {
    const client = pagedClient([{ items: [row()], hasMore: false }]);
    const userDirectory = { upsert: vi.fn() };
    const scheduler = startUserDirectoryReconciliationScheduler(
      { enabled: true, intervalHours: 12 },
      { client, userDirectory, lock: runLock as unknown as Pick<RedisDistributedLock, 'withLock'> },
    );
    await scheduler.tick();
    scheduler.stop();

    // Una al boot + la explicita.
    expect(runLock.withLock).toHaveBeenCalledTimes(2);
    expect(userDirectory.upsert).toHaveBeenCalled();
  });

  it('apagado por config no programa nada', async () => {
    const lock = { withLock: vi.fn() };
    const scheduler = startUserDirectoryReconciliationScheduler(
      { enabled: false, intervalHours: 12 },
      { client: pagedClient([]), userDirectory: { upsert: vi.fn() }, lock },
    );
    await scheduler.tick();
    expect(lock.withLock).not.toHaveBeenCalled();
  });
});

const tokens = { getToken: vi.fn().mockResolvedValue('svc-token') } as unknown as ServiceTokenClient;

function stubFetch(body: unknown, status = 200) {
  const fetchMock = vi.fn(async () => new Response(JSON.stringify(body), { status }));
  vi.stubGlobal('fetch', fetchMock);
  return fetchMock;
}

describe('HttpUserDirectoryReconciliationClient', () => {
  it('pide el endpoint interno de Auth con token de servicio y mapea camel o Pascal', async () => {
    const fetchMock = stubFetch({
      items: [
        {
          tenantId: 't-1',
          userId: 'u-1',
          actorType: 'TenantAdmin',
          isActive: true,
          displayName: 'Ana Lopez',
          email: 'ana@x.test',
          avatarFileId: 'AAAAAAAA-0000-0000-0000-000000000001',
        },
        {
          TenantId: 't-1',
          UserId: 'u-2',
          ActorType: 'CustomerPortal',
          IsActive: false,
          DisplayName: 'C',
          Email: 'c@x.test',
          AvatarFileId: null,
        },
      ],
      page: 1,
      size: 2,
      totalCount: 3,
    });

    const page = await new HttpUserDirectoryReconciliationClient(tokens).listPage(1, 2);

    expect(String((fetchMock.mock.calls[0] as unknown[])[0])).toMatch(
      /\/internal\/users\/directory\/reconciliation\?page=1&size=2$/,
    );
    expect(page).toEqual({
      hasMore: true,
      items: [
        {
          tenantId: 't-1',
          userId: 'u-1',
          actorType: 'TenantAdmin',
          isActive: true,
          displayName: 'Ana Lopez',
          email: 'ana@x.test',
          avatarFileId: 'aaaaaaaa-0000-0000-0000-000000000001',
        },
        {
          tenantId: 't-1',
          userId: 'u-2',
          actorType: 'CustomerPortal',
          isActive: false,
          displayName: 'C',
          email: 'c@x.test',
          avatarFileId: null,
        },
      ],
    });
  });

  it('un status no exitoso devuelve null (fail-soft)', async () => {
    stubFetch({}, 403);
    expect(await new HttpUserDirectoryReconciliationClient(tokens).listPage(1, 200)).toBeNull();
  });
});

describe('reconciliacion de clientes — foto', () => {
  it('el cliente HTTP distingue foto, null y campo ausente (Customer.Api viejo)', async () => {
    stubFetch({
      items: [
        {
          tenantId: 't',
          customerId: 'c-1',
          displayName: 'A',
          primaryEmail: 'a@x',
          status: 'Active',
          avatarFileId: 'F-1',
        },
        {
          tenantId: 't',
          customerId: 'c-2',
          displayName: 'B',
          primaryEmail: 'b@x',
          status: 'Archived',
          avatarFileId: null,
        },
        { tenantId: 't', customerId: 'c-3', displayName: 'C', primaryEmail: 'c@x', status: 'Active' },
      ],
      page: 1,
      size: 200,
      totalCount: 3,
    });

    const page = await new HttpCustomerReconciliationClient(tokens).listPage(1, 200);

    expect(page!.items.map((i) => i.avatarFileId)).toEqual(['f-1', null, undefined]);
    expect('avatarFileId' in page!.items[2]!).toBe(false);
  });

  it('el scheduler pasa la foto al upsert y no la toca si el campo no vino', async () => {
    const customerDirectory = { upsert: vi.fn() } as unknown as CustomerDirectoryRepository;
    const client = {
      listPage: vi.fn().mockResolvedValue({
        hasMore: false,
        items: [
          {
            tenantId: 't',
            customerId: 'c-1',
            displayName: 'A',
            primaryEmail: 'a@x',
            status: 'Active',
            avatarFileId: 'f-1',
          },
          { tenantId: 't', customerId: 'c-3', displayName: 'C', primaryEmail: 'c@x', status: 'Active' },
        ],
      }),
    } as unknown as HttpCustomerReconciliationClient;
    const lock = runLock as unknown as RedisDistributedLock;

    const scheduler = startCustomerReconciliationScheduler(
      { enabled: true, intervalHours: 12 },
      { client, customerDirectory, lock },
    );
    await vi.waitFor(() => expect(customerDirectory.upsert).toHaveBeenCalledTimes(2));
    scheduler.stop();

    const calls = vi.mocked(customerDirectory.upsert).mock.calls.map((c) => c[0]);
    expect(calls[0]).toMatchObject({ customerId: 'c-1', avatarFileId: 'f-1' });
    expect('avatarFileId' in calls[1]!).toBe(false);
  });
});
