import type { FastifyInstance } from 'fastify';
import { z } from 'zod';
import { listConversations } from '../../../application/use-cases/list-conversations.js';
import { getMessages } from '../../../application/use-cases/get-messages.js';
import { markMessagesRead } from '../../../application/use-cases/mark-messages-read.js';
import type { AppContainer } from '../../../infrastructure/container.js';

const ListQuerySchema = z.object({
  page: z.coerce.number().int().min(1).default(1),
  size: z.coerce.number().int().min(1).max(100).default(20),
  includeArchived: z.coerce.boolean().optional(),
});

const GetMessagesQuerySchema = z.object({
  before: z.string().datetime().optional(),
  take: z.coerce.number().int().min(1).max(100).default(50),
});

const MarkReadBodySchema = z.object({
  lastReadMessageId: z.string().uuid(),
});

export async function registerConversationRoutes(
  app: FastifyInstance,
  container: AppContainer,
): Promise<void> {
  // GET /communication/conversations
  app.get('/communication/conversations', { preHandler: [app.authenticate] }, async (request, reply) => {
    const principal = request.principal!;
    const query = ListQuerySchema.parse(request.query);
    const result = await listConversations(
      {
        tenantId: principal.tenantId,
        userId: principal.userId,
        page: query.page,
        size: query.size,
        includeArchived: query.includeArchived ?? false,
      },
      container,
    );
    if (!result.isSuccess) {
      return reply.code(400).send({ code: result.error.code, message: result.error.message });
    }
    return reply.send(result.value);
  });

  // GET /communication/conversations/:id/messages
  app.get(
    '/communication/conversations/:id/messages',
    { preHandler: [app.authenticate] },
    async (request, reply) => {
      const principal = request.principal!;
      const params = z.object({ id: z.string().uuid() }).parse(request.params);
      const query = GetMessagesQuerySchema.parse(request.query);
      const result = await getMessages(
        {
          tenantId: principal.tenantId,
          conversationId: params.id,
          requesterUserId: principal.userId,
          ...(query.before !== undefined ? { beforeUtc: query.before } : {}),
          take: query.take,
        },
        container,
      );
      if (!result.isSuccess) {
        return reply.code(400).send({ code: result.error.code, message: result.error.message });
      }
      return reply.send(result.value);
    },
  );

  // POST /communication/conversations/:id/read
  app.post(
    '/communication/conversations/:id/read',
    { preHandler: [app.authenticate] },
    async (request, reply) => {
      const principal = request.principal!;
      const params = z.object({ id: z.string().uuid() }).parse(request.params);
      const body = MarkReadBodySchema.parse(request.body);
      const result = await markMessagesRead(
        {
          tenantId: principal.tenantId,
          conversationId: params.id,
          userUserId: principal.userId,
          lastReadMessageId: body.lastReadMessageId,
        },
        container,
      );
      if (!result.isSuccess) {
        return reply.code(400).send({ code: result.error.code, message: result.error.message });
      }
      return reply.send(result.value);
    },
  );

  // GET /communication/conversations/:id/messages/search — NUNCA modelado en el
  // legacy. Se reserva 501 explicito para documentar la intencion sin bloquear
  // la migracion del FE.
  app.get(
    '/communication/conversations/:id/messages/search',
    { preHandler: [app.authenticate] },
    async (_request, reply) => {
      return reply
        .code(501)
        .send({ code: 'Chat.Search.NotImplemented', message: 'Full-text message search arrives in a later phase.' });
    },
  );
}
