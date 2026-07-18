import type { FastifyInstance } from 'fastify';
import { z } from 'zod';
import { listConversations } from '../../../application/use-cases/list-conversations.js';
import { getMessages } from '../../../application/use-cases/get-messages.js';
import { markMessagesRead } from '../../../application/use-cases/mark-messages-read.js';
import { searchMessages } from '../../../application/use-cases/search-messages.js';
import type { AppContainer } from '../../../infrastructure/container.js';

const ListQuerySchema = z.object({
  page: z.coerce.number().int().min(1).default(1),
  size: z.coerce.number().int().min(1).max(100).default(20),
  includeArchived: z.coerce.boolean().optional(),
});

const GetMessagesQuerySchema = z.object({
  before: z.string().datetime().optional(),
  // `since`: cursor de backfill al reconectar — ver docblock en get-messages.ts.
  since: z.string().datetime().optional(),
  take: z.coerce.number().int().min(1).max(100).default(50),
});

const MarkReadBodySchema = z.object({
  lastReadMessageId: z.string().uuid(),
});

const SearchQuerySchema = z.object({
  q: z.string().min(2).max(200),
  limit: z.coerce.number().int().min(1).max(200).optional(),
});
const ConversationIdParams = z.object({ id: z.string().uuid() });

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
          ...(query.since !== undefined ? { afterUtc: query.since } : {}),
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

  // GET /communication/conversations/:id/messages/search — Fase Backend 9.
  // Query LIKE-based (no Full-Text catalog en el entorno actual, ver
  // docblock en searchMessages y en PrismaMessageRepository.searchByBody).
  app.get(
    '/communication/conversations/:id/messages/search',
    { preHandler: [app.authenticate] },
    async (request, reply) => {
      const principal = request.principal!;
      const params = ConversationIdParams.parse(request.params);
      const query = SearchQuerySchema.parse(request.query);
      const result = await searchMessages(
        {
          tenantId: principal.tenantId,
          conversationId: params.id,
          actorUserId: principal.userId,
          query: query.q,
          ...(query.limit !== undefined ? { limit: query.limit } : {}),
        },
        container,
      );
      if (!result.isSuccess) {
        const status = result.error.code === 'Chat.Conversation.NotFound' ? 404 : 400;
        return reply.code(status).send({ code: result.error.code, message: result.error.message });
      }
      return reply.send(result.value);
    },
  );
}
