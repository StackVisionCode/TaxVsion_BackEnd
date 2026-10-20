import type { FastifyInstance } from 'fastify';
import { randomUUID } from 'node:crypto';
import { z } from 'zod';
import {
  deleteNotifications,
  listNotifications,
  listNotificationsSince,
  markAllNotificationsRead,
  markNotificationRead,
  markNotificationsRead,
  markNotificationsUnread,
  type BulkNotificationResult,
} from '../../../application/use-cases/notification-queries.js';
import { NotificationSocketEvents } from '../../../contracts/socket/notification-socket-events.js';
import { CommunicationPermissions } from '../../../domain/shared/permissions.js';
import { requirePermission } from '../plugins/require-permission.js';
import { OptionalBooleanQuery } from '../schemas/strict-boolean-query.js';
import type { AppContainer } from '../../../infrastructure/container.js';

export const NotificationListQuerySchema = z.object({
  page: z.coerce.number().int().min(1).default(1),
  size: z.coerce.number().int().min(1).max(100).default(20),
  unreadOnly: OptionalBooleanQuery,
});

/**
 * Cursor por timestamp — simple, estable y compatible con `CreatedAtUtc desc` del índice
 * existente. El cliente guarda el `createdAtUtc` del item más nuevo que vio y lo manda
 * como `sinceUtc`; el server devuelve lo que llegó después, orden asc, hasta `limit`.
 * Usado para resync tras un corte de socket (§10 del MD de notificaciones).
 */
export const NotificationSinceQuerySchema = z.object({
  sinceUtc: z.string().datetime({ offset: true }),
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
          ...(query.unreadOnly !== undefined ? { unreadOnly: query.unreadOnly } : {}),
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
      emitBulkState(container, principal.tenantId, principal.userId, request.id, result.value);
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
      emitBulkState(container, principal.tenantId, principal.userId, request.id, result.value);
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
      emitBulkState(container, principal.tenantId, principal.userId, request.id, result.value);
      return reply.send(result.value);
    },
  );
}

function emitBulkState(
  container: AppContainer,
  tenantId: string,
  userId: string,
  correlationId: string,
  result: BulkNotificationResult,
): void {
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
