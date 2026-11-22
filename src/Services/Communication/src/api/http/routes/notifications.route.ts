import type { FastifyInstance } from 'fastify';
import { randomUUID } from 'node:crypto';
import { z } from 'zod';
import {
  applyNotificationsBulkByFilter,
  deleteNotifications,
  getNotification,
  getNotificationBySource,
  listNotifications,
  listNotificationsSince,
  markAllNotificationsRead,
  markNotificationRead,
  markNotificationsRead,
  markNotificationsUnread,
  type BulkNotificationResult,
} from '../../../application/use-cases/notification-queries.js';
import {
  NotificationSocketEvents,
  type NotificationBulkAction,
  type NotificationBulkAppliedDto,
} from '../../../contracts/socket/notification-socket-events.js';
import type { RealtimeEmitter } from '../../../application/ports/realtime-emitter.js';
import { CommunicationPermissions } from '../../../domain/shared/permissions.js';
import { requirePermission } from '../plugins/require-permission.js';
import { OptionalBooleanQuery } from '../schemas/strict-boolean-query.js';
import type { AppContainer } from '../../../infrastructure/container.js';
import { config } from '../../../infrastructure/config.js';
import { sendRateLimited } from '../../../infrastructure/http/rate-limit-rejection.js';
import { CommunicationRateLimitPolicyNames } from '../../../domain/rate-limit/rate-limit-policies.js';
import {
  NOTIFICATION_CATEGORIES,
  type NotificationListFilter,
} from '../../../domain/notifications/notification-category.js';

const NotificationCategorySchema = z.enum(NOTIFICATION_CATEGORIES);
// Texto de búsqueda: 2 a 64 caracteres tras recortar. Nunca se registra en los logs.
const NotificationSearchSchema = z.string().trim().min(2).max(64);

export const NotificationListQuerySchema = z.object({
  page: z.coerce.number().int().min(1).default(1),
  size: z.coerce.number().int().min(1).max(100).default(20),
  unreadOnly: OptionalBooleanQuery,
  category: NotificationCategorySchema.optional(),
  q: NotificationSearchSchema.optional(),
});

/** Mismo filtro que la lista, en JSON y estricto. */
const NotificationFilterBody = z.strictObject({
  unreadOnly: z.boolean().optional(),
  category: NotificationCategorySchema.optional(),
  q: NotificationSearchSchema.optional(),
});

const BulkByFilterBody = z.strictObject({
  action: z.enum(['read', 'unread', 'dismissed']),
  filter: NotificationFilterBody,
  upToCreatedAtUtc: z.string().datetime({ offset: true }),
});

/**
 * Cursor por timestamp — simple, estable y compatible con `CreatedAtUtc desc` del índice
 * existente. El cliente guarda el `createdAtUtc` del item más nuevo que vio y lo manda
 * como `sinceUtc`; el server devuelve lo que llegó después, orden asc, hasta `limit`.
 * Usado para resync tras un corte de socket (§10 del MD de notificaciones).
 */
export const NotificationSinceQuerySchema = z.object({
  sinceUtc: z.string().datetime({ offset: true }),
  // Opcional: id del último item visto; desempata filas del mismo milisegundo.
  sinceId: z.string().uuid().optional(),
  limit: z.coerce.number().int().min(1).max(200).default(50),
});

const IdParams = z.object({ id: z.string().uuid() });
const MarkAllReadBody = z.object({
  upToCreatedAtUtc: z.string().datetime({ offset: true }).optional(),
});

/** 1–200 uuids: el tope lo valida el use case (BulkTooLarge) para que el error sea consistente. */
const BulkIdsBody = z.object({
  ids: z.array(z.string().uuid()).min(1).max(200),
});

export async function registerNotificationRoutes(
  app: FastifyInstance,
  container: AppContainer,
): Promise<void> {
  app.get(
    '/communication/notifications',
    {
      preHandler: [app.authenticate, requirePermission(container, CommunicationPermissions.NotificationRead)],
    },
    async (request, reply) => {
      const principal = request.principal!;
      const query = NotificationListQuerySchema.parse(request.query);
      const result = await listNotifications(
        {
          tenantId: principal.tenantId,
          userId: principal.userId,
          page: query.page,
          size: query.size,
          ...filterOf(query),
        },
        container,
      );
      if (!result.isSuccess) {
        return reply.code(400).send({ code: result.error.code, message: result.error.message });
      }
      return reply.send(result.value);
    },
  );

  // Resync tras reconexión de socket: el cliente guarda el `createdAtUtc` del último
  // item que vio y pide "dame lo que llegó después". Suple la falta de
  // `restoreSession` del Redis adapter de Socket.IO (ver §10 del MD).
  app.get(
    '/communication/notifications/since',
    {
      preHandler: [app.authenticate, requirePermission(container, CommunicationPermissions.NotificationRead)],
    },
    async (request, reply) => {
      const principal = request.principal!;
      const query = NotificationSinceQuerySchema.parse(request.query);
      const result = await listNotificationsSince(
        {
          tenantId: principal.tenantId,
          userId: principal.userId,
          sinceUtc: new Date(query.sinceUtc),
          ...(query.sinceId ? { sinceId: query.sinceId } : {}),
          limit: query.limit,
        },
        container,
      );
      if (!result.isSuccess) {
        return reply.code(400).send({ code: result.error.code, message: result.error.message });
      }
      return reply.send(result.value);
    },
  );

  app.get(
    '/communication/notifications/unread-count',
    {
      preHandler: [app.authenticate, requirePermission(container, CommunicationPermissions.NotificationRead)],
    },
    async (request, reply) => {
      const principal = request.principal!;
      const count = await container.notifications.countUnread(principal.tenantId, principal.userId);
      return reply.send({ count, asOfUtc: new Date().toISOString() });
    },
  );

  // Fila creada por un evento (clic de un push): mismo DTO y mismo 404 que el detalle por id.
  app.get(
    '/communication/notifications/by-source/:id',
    {
      preHandler: [app.authenticate, requirePermission(container, CommunicationPermissions.NotificationRead)],
    },
    async (request, reply) => {
      const principal = request.principal;
      if (!principal)
        return reply.code(401).send({ code: 'Auth.Unauthenticated', message: 'Authentication required.' });
      const params = IdParams.safeParse(request.params);
      const result = params.success
        ? await getNotificationBySource(
            { tenantId: principal.tenantId, userId: principal.userId, sourceEventId: params.data.id },
            container,
          )
        : null;
      if (!result?.isSuccess) {
        return reply.code(404).send({ code: 'Notification.NotFound', message: 'Notification not found.' });
      }
      return reply.send(result.value);
    },
  );

  // Detalle por id: mismo DTO que la lista, sin marcar leída. Todo fallo responde el mismo 404.
  app.get(
    '/communication/notifications/:id',
    {
      preHandler: [app.authenticate, requirePermission(container, CommunicationPermissions.NotificationRead)],
    },
    async (request, reply) => {
      const principal = request.principal;
      if (!principal)
        return reply.code(401).send({ code: 'Auth.Unauthenticated', message: 'Authentication required.' });
      const params = IdParams.safeParse(request.params);
      const result = params.success
        ? await getNotification(
            { tenantId: principal.tenantId, userId: principal.userId, notificationId: params.data.id },
            container,
          )
        : null;
      if (!result?.isSuccess) {
        return reply.code(404).send({ code: 'Notification.NotFound', message: 'Notification not found.' });
      }
      return reply.send(result.value);
    },
  );

  app.post(
    '/communication/notifications/read-all',
    {
      preHandler: [app.authenticate, requirePermission(container, CommunicationPermissions.NotificationRead)],
    },
    async (request, reply) => {
      const principal = request.principal!;
      const body = MarkAllReadBody.parse(request.body ?? {});
      const result = await markAllNotificationsRead(
        {
          tenantId: principal.tenantId,
          userId: principal.userId,
          ...(body.upToCreatedAtUtc !== undefined ? { cutoffUtc: new Date(body.upToCreatedAtUtc) } : {}),
        },
        container,
      );
      if (!result.isSuccess) {
        return reply.code(400).send({ code: result.error.code, message: result.error.message });
      }
      emitReadAllState(container, principal.tenantId, principal.userId, request.id, result.value);
      return reply.send(result.value);
    },
  );

  app.post(
    '/communication/notifications/:id/read',
    {
      preHandler: [app.authenticate, requirePermission(container, CommunicationPermissions.NotificationRead)],
    },
    async (request, reply) => {
      const principal = request.principal!;
      const params = IdParams.parse(request.params);
      const result = await markNotificationRead(
        { tenantId: principal.tenantId, userId: principal.userId, notificationId: params.id },
        container,
      );
      if (!result.isSuccess) {
        return reply.code(400).send({ code: result.error.code, message: result.error.message });
      }
      emitReadState(container, principal.tenantId, principal.userId, request.id, result.value);
      return reply.send(result.value);
    },
  );

  // Bulk endpoints — una sola transacción + un solo socket de unread_count por lote.
  // El cliente deja el cambio local optimista; el socket confirma el conteo global.
  app.post(
    '/communication/notifications/mark-many-read',
    {
      preHandler: [app.authenticate, requirePermission(container, CommunicationPermissions.NotificationRead)],
    },
    async (request, reply) => {
      const principal = request.principal!;
      const body = BulkIdsBody.parse(request.body ?? {});
      const result = await markNotificationsRead(
        { tenantId: principal.tenantId, userId: principal.userId, ids: body.ids },
        container,
      );
      if (!result.isSuccess) {
        return reply.code(400).send({ code: result.error.code, message: result.error.message });
      }
      emitBulkState(
        container,
        principal.tenantId,
        principal.userId,
        request.id,
        result.value,
        'read',
        body.ids,
      );
      return reply.send(result.value);
    },
  );

  app.post(
    '/communication/notifications/mark-many-unread',
    {
      preHandler: [app.authenticate, requirePermission(container, CommunicationPermissions.NotificationRead)],
    },
    async (request, reply) => {
      const principal = request.principal!;
      const body = BulkIdsBody.parse(request.body ?? {});
      const result = await markNotificationsUnread(
        { tenantId: principal.tenantId, userId: principal.userId, ids: body.ids },
        container,
      );
      if (!result.isSuccess) {
        return reply.code(400).send({ code: result.error.code, message: result.error.message });
      }
      emitBulkState(
        container,
        principal.tenantId,
        principal.userId,
        request.id,
        result.value,
        'unread',
        body.ids,
      );
      return reply.send(result.value);
    },
  );

  // Todo lo que cumple el filtro hasta el corte de la lista; el socket lleva el filtro, no los ids.
  app.post(
    '/communication/notifications/bulk-by-filter',
    {
      preHandler: [app.authenticate, requirePermission(container, CommunicationPermissions.NotificationRead)],
    },
    async (request, reply) => {
      const principal = request.principal;
      if (!principal)
        return reply.code(401).send({ code: 'Auth.Unauthenticated', message: 'Authentication required.' });
      const body = BulkByFilterBody.parse(request.body ?? {});
      const policy = CommunicationRateLimitPolicyNames.NotificationsBulkByFilter;
      const decision = await container.httpRateLimiter.allow({
        key: `comm:rl:${policy}:${principal.tenantId}:${principal.userId}`,
        policy,
        maxPerWindow: config.rateLimit.notificationsBulkByFilter.maxPerWindow,
        windowSeconds: config.rateLimit.notificationsBulkByFilter.windowSeconds,
      });
      if (!decision.allowed) return sendRateLimited(reply, decision.retryAfterSeconds, policy);

      const filter = filterOf(body.filter);
      const result = await applyNotificationsBulkByFilter(
        {
          tenantId: principal.tenantId,
          userId: principal.userId,
          action: body.action,
          filter,
          upToCreatedAtUtc: new Date(body.upToCreatedAtUtc),
        },
        container,
      );
      if (!result.isSuccess) {
        return reply.code(400).send({ code: result.error.code, message: result.error.message });
      }
      // Primero las filas, después el contador.
      if (result.value.affectedCount > 0) {
        emitNotificationBulkApplied(container.emitter, {
          tenantId: principal.tenantId,
          userId: principal.userId,
          correlationId: request.id,
          action: body.action,
          ids: [],
          asOfUtc: result.value.asOfUtc,
          byFilter: { filter, cutoffUtc: result.value.cutoffUtc, affectedCount: result.value.affectedCount },
        });
      }
      emitUnreadCount(
        container,
        principal.tenantId,
        principal.userId,
        request.id,
        result.value.unreadCount,
        result.value.asOfUtc,
      );
      return reply.send(result.value);
    },
  );

  app.post(
    '/communication/notifications/delete-many',
    {
      preHandler: [app.authenticate, requirePermission(container, CommunicationPermissions.NotificationRead)],
    },
    async (request, reply) => {
      const principal = request.principal!;
      const body = BulkIdsBody.parse(request.body ?? {});
      const result = await deleteNotifications(
        { tenantId: principal.tenantId, userId: principal.userId, ids: body.ids },
        container,
      );
      if (!result.isSuccess) {
        return reply.code(400).send({ code: result.error.code, message: result.error.message });
      }
      emitBulkState(
        container,
        principal.tenantId,
        principal.userId,
        request.id,
        result.value,
        'dismissed',
        body.ids,
      );
      return reply.send(result.value);
    },
  );
}

/** Avisa a todas las sesiones del usuario qué filas cambiaron. La comparte el descarte por socket. */
export function emitNotificationBulkApplied(
  emitter: Pick<RealtimeEmitter, 'emitToUser'> | undefined,
  input: {
    tenantId: string;
    userId: string;
    correlationId: string;
    action: NotificationBulkAction;
    ids: readonly string[];
    asOfUtc: string;
    /** Acción por filtro: sin ids (pueden ser miles); el cliente vuelve a pedir su página. */
    byFilter?: { filter: NotificationListFilter; cutoffUtc: string; affectedCount: number };
  },
): void {
  const payload: NotificationBulkAppliedDto = {
    action: input.action,
    notificationIds: Array.from(new Set(input.ids)),
    asOfUtc: input.asOfUtc,
    ...(input.byFilter
      ? {
          scope: 'filter' as const,
          filter: input.byFilter.filter,
          cutoffUtc: input.byFilter.cutoffUtc,
          affectedCount: input.byFilter.affectedCount,
        }
      : {}),
  };
  emitter?.emitToUser({
    tenantId: input.tenantId,
    userId: input.userId,
    event: NotificationSocketEvents.BulkApplied,
    envelope: socketEnvelope(input.correlationId, payload),
  });
}

function emitBulkState(
  container: AppContainer,
  tenantId: string,
  userId: string,
  correlationId: string,
  result: BulkNotificationResult,
  action: NotificationBulkAction,
  ids: readonly string[],
): void {
  // Primero las filas, después el contador.
  if (result.affectedCount > 0) {
    emitNotificationBulkApplied(container.emitter, {
      tenantId,
      userId,
      correlationId,
      action,
      ids,
      asOfUtc: result.asOfUtc,
    });
  }
  emitUnreadCount(container, tenantId, userId, correlationId, result.unreadCount, result.asOfUtc);
}

function emitReadState(
  container: AppContainer,
  tenantId: string,
  userId: string,
  correlationId: string,
  result: { notificationId: string; unreadCount: number; asOfUtc: string },
): void {
  container.emitter?.emitToUser({
    tenantId,
    userId,
    event: NotificationSocketEvents.ReadConfirmed,
    envelope: socketEnvelope(correlationId, {
      notificationId: result.notificationId,
      asOfUtc: result.asOfUtc,
    }),
  });
  emitUnreadCount(container, tenantId, userId, correlationId, result.unreadCount, result.asOfUtc);
}

function emitReadAllState(
  container: AppContainer,
  tenantId: string,
  userId: string,
  correlationId: string,
  result: { cutoffUtc: string; unreadCount: number; asOfUtc: string },
): void {
  container.emitter?.emitToUser({
    tenantId,
    userId,
    event: NotificationSocketEvents.ReadAllConfirmed,
    envelope: socketEnvelope(correlationId, { cutoffUtc: result.cutoffUtc, asOfUtc: result.asOfUtc }),
  });
  emitUnreadCount(container, tenantId, userId, correlationId, result.unreadCount, result.asOfUtc);
}

function emitUnreadCount(
  container: AppContainer,
  tenantId: string,
  userId: string,
  correlationId: string,
  count: number,
  asOfUtc: string,
): void {
  container.emitter?.emitToUser({
    tenantId,
    userId,
    event: NotificationSocketEvents.UnreadCountChanged,
    envelope: socketEnvelope(correlationId, { count, asOfUtc }),
  });
}

function socketEnvelope<T>(correlationId: string, payload: T) {
  return {
    eventId: randomUUID(),
    correlationId,
    emittedAtUtc: new Date().toISOString(),
    payload,
  };
}

/** Solo los filtros presentes (exactOptionalPropertyTypes). */
function filterOf(input: {
  unreadOnly?: boolean | undefined;
  category?: NotificationListFilter['category'] | undefined;
  q?: string | undefined;
}): NotificationListFilter {
  return {
    ...(input.unreadOnly !== undefined ? { unreadOnly: input.unreadOnly } : {}),
    ...(input.category !== undefined ? { category: input.category } : {}),
    ...(input.q !== undefined ? { q: input.q } : {}),
  };
}
