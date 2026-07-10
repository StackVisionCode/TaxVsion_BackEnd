import type { FastifyInstance } from 'fastify';
import { z } from 'zod';
import { listNotifications, markNotificationRead } from '../../../application/use-cases/notification-queries.js';
import type { AppContainer } from '../../../infrastructure/container.js';

const ListQuery = z.object({
  page: z.coerce.number().int().min(1).default(1),
  size: z.coerce.number().int().min(1).max(100).default(20),
  unreadOnly: z.coerce.boolean().optional(),
});

const IdParams = z.object({ id: z.string().uuid() });

export async function registerNotificationRoutes(app: FastifyInstance, container: AppContainer): Promise<void> {
  app.get('/communication/notifications', { preHandler: [app.authenticate] }, async (request, reply) => {
    const principal = request.principal!;
    const query = ListQuery.parse(request.query);
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
  });

  app.get(
    '/communication/notifications/unread-count',
    { preHandler: [app.authenticate] },
    async (request, reply) => {
      const principal = request.principal!;
      const count = await container.notifications.countUnread(principal.tenantId, principal.userId);
      return reply.send({ count });
    },
  );

  app.post(
    '/communication/notifications/:id/read',
    { preHandler: [app.authenticate] },
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
      return reply.send(result.value);
    },
  );
}
