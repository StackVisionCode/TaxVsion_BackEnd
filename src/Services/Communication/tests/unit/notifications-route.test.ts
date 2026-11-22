import Fastify, { type FastifyReply, type FastifyRequest } from 'fastify';
import { describe, expect, it, vi } from 'vitest';
import { randomUUID } from 'node:crypto';
import { registerNotificationRoutes } from '../../src/api/http/routes/notifications.route.js';
import type { AppContainer } from '../../src/infrastructure/container.js';
import type { AuthenticatedPrincipal } from '../../src/infrastructure/jwks/jwt-verifier.js';
import { toNotificationDto } from '../../src/application/use-cases/notification-dto.js';
import { Notification, type NotificationSnapshot } from '../../src/domain/notifications/notification.js';
import { registerHttpErrorHandler } from '../../src/infrastructure/http/http-error-handler.js';

const BULK_APPLIED = 'notification.bulk.applied';
const UNREAD_COUNT_CHANGED = 'notification.unread_count.changed';

interface EmittedEvent {
  tenantId: string;
  userId: string;
  event: string;
  envelope: {
    eventId: string;
    correlationId: string;
    emittedAtUtc: string;
    payload: Record<string, unknown>;
  };
}

// userId aleatorio por prueba: checkPermission guarda la proyección en una caché de módulo.
function setup(
  affectedCount: number,
  rows: (tenantId: string, userId: string) => NotificationSnapshot[] = () => [],
) {
  const tenantId = randomUUID();
  const userId = randomUUID();
  const emitted: EmittedEvent[] = [];
  const emitToUser = vi.fn((input: EmittedEvent) => {
    emitted.push(input);
  });
  const stored = rows(tenantId, userId);
  const notifications = {
    // Filtra por oficina y usuario, igual que el repositorio Prisma.
    findById: vi.fn(async (t: string, id: string, u: string) => {
      const row = stored.find((r) => r.id === id && r.tenantId === t && r.userId === u);
      return row ? Notification.rehydrate(row) : null;
    }),
    findBySourceEvent: vi.fn(async (t: string, sourceEventId: string, u: string) => {
      const row = stored.find((r) => r.sourceEventId === sourceEventId && r.tenantId === t && r.userId === u);
      return row ? Notification.rehydrate(row) : null;
    }),
    markRead: vi.fn(async () => true),
    countUnread: vi.fn(async () => 0),
    listSinceForUser: vi.fn(async () => []),
    markManyRead: vi.fn(async () => ({ markedCount: affectedCount, unreadCount: 3 })),
    markManyUnread: vi.fn(async () => ({ unmarkedCount: affectedCount, unreadCount: 3 })),
    dismissMany: vi.fn(async () => ({ dismissedCount: affectedCount, unreadCount: 3 })),
    listForUser: vi.fn(async () => []),
    countForUser: vi.fn(async () => 0),
    bulkByFilter: vi.fn(async () => ({ affectedCount, unreadCount: 3 })),
  };
  const httpRateLimiter = { allow: vi.fn(async () => ({ allowed: true, retryAfterSeconds: 0 })) };
  const container = {
    userPermissions: {
      async findByUserId(id: string) {
        return {
          userId: id,
          tenantId,
          permissions: ['communication.notification.read'],
          permissionVersion: 1,
          roleIds: [],
          actorType: 'TenantEmployee',
          isActive: true,
          updatedAtUtc: new Date(),
        };
      },
    },
    notifications,
    httpRateLimiter,
    emitter: { emitToUser },
  } as unknown as AppContainer;
  const principal: AuthenticatedPrincipal = {
    userId,
    tenantId,
    actorType: 'TenantEmployee',
    permissions: [],
    permissionVersion: 1,
    sessionId: undefined,
    jti: undefined,
    raw: {},
  };
  return { tenantId, userId, emitted, notifications, httpRateLimiter, container, principal };
}

async function post(ctx: ReturnType<typeof setup>, action: string, ids: readonly string[]) {
  const app = Fastify();
  app.decorate('authenticate', async function authenticate(request: FastifyRequest, _reply: FastifyReply) {
    request.principal = ctx.principal;
  });
  registerHttpErrorHandler(app);
  await registerNotificationRoutes(app, ctx.container);
  await app.ready();
  const response = await app.inject({
    method: 'POST',
    url: `/communication/notifications/${action}`,
    payload: { ids },
  });
  await app.close();
  return response;
}

describe('notification bulk routes', () => {
  it('delete-many emits the affected ids to the user room', async () => {
    const ctx = setup(2);
    const a = randomUUID();
    const b = randomUUID();

    const response = await post(ctx, 'delete-many', [a, b]);

    expect(response.statusCode).toBe(200);
    const body = response.json<{ affectedCount: number; unreadCount: number; asOfUtc: string }>();
    expect(ctx.emitted.map((e) => e.event)).toEqual([BULK_APPLIED, UNREAD_COUNT_CHANGED]);
    const bulk = ctx.emitted.find((e) => e.event === BULK_APPLIED);
    expect(bulk).toMatchObject({ tenantId: ctx.tenantId, userId: ctx.userId });
    expect(bulk?.envelope.payload).toEqual({
      action: 'dismissed',
      notificationIds: [a, b],
      asOfUtc: body.asOfUtc,
    });
    expect(bulk?.envelope.correlationId).not.toBe('');
  });

  it('mark-many-read emits action read', async () => {
    const ctx = setup(1);
    const a = randomUUID();

    const response = await post(ctx, 'mark-many-read', [a]);

    const body = response.json<{ asOfUtc: string }>();
    expect(ctx.emitted.map((e) => e.event)).toEqual([BULK_APPLIED, UNREAD_COUNT_CHANGED]);
    expect(ctx.emitted.find((e) => e.event === BULK_APPLIED)?.envelope.payload).toEqual({
      action: 'read',
      notificationIds: [a],
      asOfUtc: body.asOfUtc,
    });
  });

  it('mark-many-unread emits action unread', async () => {
    const ctx = setup(1);
    const a = randomUUID();

    const response = await post(ctx, 'mark-many-unread', [a]);

    const body = response.json<{ asOfUtc: string }>();
    expect(ctx.emitted.map((e) => e.event)).toEqual([BULK_APPLIED, UNREAD_COUNT_CHANGED]);
    expect(ctx.emitted.find((e) => e.event === BULK_APPLIED)?.envelope.payload).toEqual({
      action: 'unread',
      notificationIds: [a],
      asOfUtc: body.asOfUtc,
    });
  });

  it('duplicated ids travel once', async () => {
    const ctx = setup(2);
    const a = randomUUID();
    const b = randomUUID();

    await post(ctx, 'delete-many', [a, a, b]);

    expect(ctx.emitted.find((e) => e.event === BULK_APPLIED)?.envelope.payload).toMatchObject({
      notificationIds: [a, b],
    });
  });

  it('nothing affected emits only the unread count', async () => {
    const ctx = setup(0);

    const response = await post(ctx, 'mark-many-read', [randomUUID()]);

    expect(response.statusCode).toBe(200);
    expect(ctx.emitted.map((e) => e.event)).toEqual([UNREAD_COUNT_CHANGED]);
  });

  it('the ids never go to another user', async () => {
    const ctx = setup(1);
    const foreignId = randomUUID();

    await post(ctx, 'delete-many', [foreignId]);

    expect(ctx.notifications.dismissMany).toHaveBeenCalledWith(
      expect.objectContaining({ tenantId: ctx.tenantId, userId: ctx.userId }),
    );
    expect(ctx.emitted.length).toBeGreaterThan(0);
    for (const e of ctx.emitted) {
      expect(e).toMatchObject({ tenantId: ctx.tenantId, userId: ctx.userId });
    }
  });
});

function entry(overrides: Partial<NotificationSnapshot> & Pick<NotificationSnapshot, 'tenantId' | 'userId'>) {
  const row: NotificationSnapshot = {
    id: randomUUID(),
    kind: 'signature.completed',
    priority: 'High',
    title: 'Document signed',
    body: 'The client signed the engagement letter.',
    metadata: {},
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
    sourceEventId: randomUUID(),
    sourceEventType: 'SignatureCompleted',
    correlationId: null,
    readAtUtc: null,
    dismissedAtUtc: null,
    createdAtUtc: new Date('2026-11-20T10:00:01.000Z'),
    ...overrides,
  };
  return row;
}

async function get(ctx: ReturnType<typeof setup>, url: string) {
  const app = Fastify();
  app.decorate('authenticate', async function authenticate(request: FastifyRequest, _reply: FastifyReply) {
    request.principal = ctx.principal;
  });
  registerHttpErrorHandler(app);
  await registerNotificationRoutes(app, ctx.container);
  await app.ready();
  const response = await app.inject({ method: 'GET', url });
  await app.close();
  return response;
}

const OWN_ID = randomUUID();
const OTHER_USER_ID = randomUUID();
const OTHER_TENANT_ID = randomUUID();
const DISMISSED_ID = randomUUID();

function detailSetup() {
  return setup(0, (tenantId, userId) => [
    entry({ id: OWN_ID, tenantId, userId }),
    entry({ id: OTHER_USER_ID, tenantId, userId: randomUUID() }),
    entry({ id: OTHER_TENANT_ID, tenantId: randomUUID(), userId }),
    entry({ id: DISMISSED_ID, tenantId, userId, dismissedAtUtc: new Date('2026-11-21T00:00:00.000Z') }),
  ]);
}

describe('notification detail route', () => {
  it('GET by id returns 200 with the DTO', async () => {
    const ctx = detailSetup();

    const response = await get(ctx, `/communication/notifications/${OWN_ID}`);

    expect(response.statusCode).toBe(200);
    const own = entry({ id: OWN_ID, tenantId: ctx.tenantId, userId: ctx.userId });
    expect(response.json()).toEqual(toNotificationDto(own));
  });

  it('GET by id answers the same 404 for unknown, foreign, dismissed and malformed ids', async () => {
    const ctx = detailSetup();
    const notFound = { code: 'Notification.NotFound', message: 'Notification not found.' };

    for (const id of [randomUUID(), OTHER_USER_ID, OTHER_TENANT_ID, DISMISSED_ID, 'not-a-uuid']) {
      const response = await get(ctx, `/communication/notifications/${id}`);
      expect(response.statusCode, id).toBe(404);
      expect(response.json(), id).toEqual(notFound);
    }
  });

  it('GET since and unread-count still resolve', async () => {
    const ctx = detailSetup();

    const since = await get(ctx, '/communication/notifications/since?sinceUtc=2026-11-20T00:00:00.000Z');
    const unread = await get(ctx, '/communication/notifications/unread-count');

    expect(since.statusCode).toBe(200);
    expect(since.json()).toMatchObject({ items: [], unreadCount: 0 });
    expect(unread.statusCode).toBe(200);
    expect(unread.json()).toMatchObject({ count: 0 });
    expect(ctx.notifications.findById).not.toHaveBeenCalled();
  });

  it('GET by id takes tenant and user from the token', async () => {
    const ctx = detailSetup();

    await get(ctx, `/communication/notifications/${OWN_ID}?tenantId=${randomUUID()}&userId=${randomUUID()}`);

    expect(ctx.notifications.findById).toHaveBeenCalledWith(ctx.tenantId, OWN_ID, ctx.userId);
  });

  it('GET by id has no side effects', async () => {
    const ctx = detailSetup();

    const response = await get(ctx, `/communication/notifications/${OWN_ID}`);

    expect(response.statusCode).toBe(200);
    expect(response.json<{ readAtUtc: string | null }>().readAtUtc).toBeNull();
    expect(ctx.notifications.markRead).not.toHaveBeenCalled();
    expect(ctx.emitted).toEqual([]);
  });
});

describe('notification by-source route', () => {
  const OWN_EVENT = randomUUID();
  const FOREIGN_EVENT = randomUUID();

  function sourceSetup() {
    return setup(0, (tenantId, userId) => [
      entry({ id: OWN_ID, tenantId, userId, sourceEventId: OWN_EVENT }),
      entry({ id: OTHER_USER_ID, tenantId, userId: randomUUID(), sourceEventId: FOREIGN_EVENT }),
    ]);
  }

  it('GET by-source returns 200 with the same DTO as the detail', async () => {
    const ctx = sourceSetup();

    const response = await get(ctx, `/communication/notifications/by-source/${OWN_EVENT}`);

    expect(response.statusCode).toBe(200);
    const own = entry({ id: OWN_ID, tenantId: ctx.tenantId, userId: ctx.userId, sourceEventId: OWN_EVENT });
    expect(response.json()).toEqual(toNotificationDto(own));
    expect(ctx.notifications.findBySourceEvent).toHaveBeenCalledWith(ctx.tenantId, OWN_EVENT, ctx.userId);
  });

  it('GET by-source answers 404 for another user, an unknown event and a malformed id', async () => {
    const ctx = sourceSetup();

    for (const id of [FOREIGN_EVENT, randomUUID(), 'not-a-uuid']) {
      const response = await get(ctx, `/communication/notifications/by-source/${id}`);
      expect(response.statusCode, id).toBe(404);
      expect(response.json(), id).toEqual({
        code: 'Notification.NotFound',
        message: 'Notification not found.',
      });
    }
  });
});

describe('notification list filters', () => {
  it('rejects an unknown category', async () => {
    const ctx = setup(0);

    const response = await get(ctx, '/communication/notifications?category=Gossip');

    expect(response.statusCode).toBe(400);
    expect(response.json()).toMatchObject({ code: 'Request.Invalid' });
    expect(ctx.notifications.listForUser).not.toHaveBeenCalled();
  });

  it('rejects a search outside 2 to 64 characters', async () => {
    const ctx = setup(0);

    for (const q of ['a', '%20a%20%20', 'x'.repeat(65)]) {
      const response = await get(ctx, `/communication/notifications?q=${q}`);
      expect(response.statusCode, q).toBe(400);
      expect(response.json(), q).toMatchObject({ code: 'Request.Invalid' });
    }
    expect(ctx.notifications.listForUser).not.toHaveBeenCalled();
  });

  it('passes category and trimmed search to the page and the total, the unread count stays global', async () => {
    const ctx = setup(0);
    ctx.notifications.countForUser.mockResolvedValue(7);
    ctx.notifications.countUnread.mockResolvedValue(12);

    const response = await get(
      ctx,
      '/communication/notifications?category=Billing&q=%20INV-2026%20&unreadOnly=true',
    );

    expect(response.statusCode).toBe(200);
    expect(response.json()).toMatchObject({ total: 7, unreadCount: 12 });
    const filter = { unreadOnly: true, category: 'Billing', q: 'INV-2026' };
    expect(ctx.notifications.listForUser).toHaveBeenCalledWith(
      expect.objectContaining({ tenantId: ctx.tenantId, userId: ctx.userId, ...filter }),
    );
    expect(ctx.notifications.countForUser).toHaveBeenCalledWith({
      tenantId: ctx.tenantId,
      userId: ctx.userId,
      ...filter,
    });
    expect(ctx.notifications.countUnread).toHaveBeenCalledWith(ctx.tenantId, ctx.userId);
  });
});

async function postBulkByFilter(ctx: ReturnType<typeof setup>, payload: Record<string, unknown>) {
  const app = Fastify();
  app.decorate('authenticate', async function authenticate(request: FastifyRequest, _reply: FastifyReply) {
    request.principal = ctx.principal;
  });
  registerHttpErrorHandler(app);
  await registerNotificationRoutes(app, ctx.container);
  await app.ready();
  const response = await app.inject({
    method: 'POST',
    url: '/communication/notifications/bulk-by-filter',
    payload,
  });
  await app.close();
  return response;
}

describe('notification bulk-by-filter route', () => {
  const cutoff = '2026-11-20T10:00:00.000Z';

  it('bulkByFilter emits bulk.applied with scope filter and no ids, then the unread count', async () => {
    const ctx = setup(42);

    const response = await postBulkByFilter(ctx, {
      action: 'read',
      filter: { category: 'Billing', q: ' Smith ' },
      upToCreatedAtUtc: cutoff,
    });

    expect(response.statusCode).toBe(200);
    const body = response.json<{
      affectedCount: number;
      unreadCount: number;
      cutoffUtc: string;
      asOfUtc: string;
    }>();
    expect(body).toMatchObject({ affectedCount: 42, unreadCount: 3, cutoffUtc: cutoff });
    expect(ctx.emitted.map((e) => e.event)).toEqual([BULK_APPLIED, UNREAD_COUNT_CHANGED]);
    for (const e of ctx.emitted) expect(e).toMatchObject({ tenantId: ctx.tenantId, userId: ctx.userId });
    expect(ctx.emitted[0]?.envelope.payload).toEqual({
      action: 'read',
      notificationIds: [],
      asOfUtc: body.asOfUtc,
      scope: 'filter',
      filter: { category: 'Billing', q: 'Smith' },
      cutoffUtc: cutoff,
      affectedCount: 42,
    });
    expect(ctx.emitted[1]?.envelope.payload).toEqual({ count: 3, asOfUtc: body.asOfUtc });
    expect(ctx.notifications.bulkByFilter).toHaveBeenCalledWith(
      expect.objectContaining({
        tenantId: ctx.tenantId,
        userId: ctx.userId,
        action: 'read',
        filter: { category: 'Billing', q: 'Smith' },
        cutoffUtc: new Date(cutoff),
      }),
    );
  });

  it('nothing affected emits only the unread count', async () => {
    const ctx = setup(0);

    const response = await postBulkByFilter(ctx, {
      action: 'dismissed',
      filter: {},
      upToCreatedAtUtc: cutoff,
    });

    expect(response.statusCode).toBe(200);
    expect(ctx.emitted.map((e) => e.event)).toEqual([UNREAD_COUNT_CHANGED]);
  });

  it('rejects unknown fields, ids, a missing cutoff and an invalid filter', async () => {
    const ctx = setup(1);

    for (const payload of [
      { action: 'read', filter: {}, upToCreatedAtUtc: cutoff, ids: [randomUUID()] },
      { action: 'read', filter: { tenantId: randomUUID() }, upToCreatedAtUtc: cutoff },
      { action: 'read', filter: {} },
      { action: 'archive', filter: {}, upToCreatedAtUtc: cutoff },
      { action: 'read', filter: { category: 'Gossip' }, upToCreatedAtUtc: cutoff },
      { action: 'read', filter: { q: 'a' }, upToCreatedAtUtc: cutoff },
    ]) {
      const response = await postBulkByFilter(ctx, payload);
      expect(response.statusCode, JSON.stringify(payload)).toBe(400);
      expect(response.json()).toMatchObject({ code: 'Request.Invalid' });
    }
    expect(ctx.notifications.bulkByFilter).not.toHaveBeenCalled();
  });

  it('is limited per user', async () => {
    const ctx = setup(1);
    ctx.httpRateLimiter.allow.mockResolvedValueOnce({ allowed: false, retryAfterSeconds: 30 });

    const response = await postBulkByFilter(ctx, { action: 'read', filter: {}, upToCreatedAtUtc: cutoff });

    expect(response.statusCode).toBe(429);
    expect(response.json()).toMatchObject({ code: 'RateLimit.Exceeded' });
    expect(ctx.httpRateLimiter.allow).toHaveBeenCalledWith(
      expect.objectContaining({
        key: expect.stringContaining(`${ctx.tenantId}:${ctx.userId}`),
        maxPerWindow: 10,
        windowSeconds: 60,
      }),
    );
    expect(ctx.notifications.bulkByFilter).not.toHaveBeenCalled();
    expect(ctx.emitted).toEqual([]);
  });
});
