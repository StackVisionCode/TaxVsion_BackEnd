import type { FastifyInstance } from 'fastify';
import { z } from 'zod';
import { listConversations } from '../../../application/use-cases/list-conversations.js';
import { getMessages } from '../../../application/use-cases/get-messages.js';
import { markMessagesRead } from '../../../application/use-cases/mark-messages-read.js';
import { searchMessages } from '../../../application/use-cases/search-messages.js';
import { getAttachmentMetadata } from '../../../application/use-cases/get-attachment-metadata.js';
import { getAttachmentDownloadUrl } from '../../../application/use-cases/get-attachment-download-url.js';
import { createAttachmentUpload } from '../../../application/use-cases/create-attachment-upload.js';
import { completeAttachmentUpload } from '../../../application/use-cases/complete-attachment-upload.js';
import { CloudStorageUploadError } from '../../../infrastructure/cloudstorage/http-cloudstorage-upload-client.js';
import type { AppContainer } from '../../../infrastructure/container.js';

const AttachmentParams = z.object({ id: z.string().uuid(), fileId: z.string().uuid() });
const ConversationParam = z.object({ id: z.string().uuid() });
const CreateUploadBody = z.object({
  originalName: z.string().min(1).max(255),
  contentType: z.string().min(1).max(255),
  sizeBytes: z.number().int().positive(),
});

// Estados de acceso a un adjunto → HTTP. El code queda para diagnostico; el
// front muestra el message, nunca el code.
function attachmentHttpStatus(code: string): number {
  switch (code) {
    case 'Chat.Attachment.NotFound':
    case 'Chat.Conversation.NotFound':
      return 404;
    case 'Chat.Conversation.NotParticipant':
    case 'Chat.Attachment.Unavailable':
      return 403;
    case 'Chat.Attachment.Deleted':
      return 410;
    case 'Chat.Attachment.Pending':
      return 409;
    default:
      return 400;
  }
}

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

  // GET /communication/conversations/:id/attachments/:fileId — metadata + estado
  // de escaneo de un adjunto, autorizado por membresia de conversacion.
  app.get(
    '/communication/conversations/:id/attachments/:fileId',
    { preHandler: [app.authenticate] },
    async (request, reply) => {
      const principal = request.principal!;
      const params = AttachmentParams.parse(request.params);
      const result = await getAttachmentMetadata(
        {
          tenantId: principal.tenantId,
          userId: principal.userId,
          conversationId: params.id,
          fileId: params.fileId,
        },
        container,
      );
      if (!result.isSuccess) {
        return reply
          .code(attachmentHttpStatus(result.error.code))
          .send({ code: result.error.code, message: result.error.message });
      }
      return reply.send(result.value);
    },
  );

  // POST /communication/conversations/:id/attachments/upload — inicia una subida
  // mediada (blob ownerType=Communication); el browser sube directo a MinIO con la
  // URL presignada que se devuelve.
  app.post(
    '/communication/conversations/:id/attachments/upload',
    { preHandler: [app.authenticate] },
    async (request, reply) => {
      const principal = request.principal!;
      const params = ConversationParam.parse(request.params);
      const body = CreateUploadBody.parse(request.body);
      let result;
      try {
        result = await createAttachmentUpload(
          {
            tenantId: principal.tenantId,
            userId: principal.userId,
            conversationId: params.id,
            originalName: body.originalName,
            contentType: body.contentType,
            sizeBytes: body.sizeBytes,
          },
          container,
        );
      } catch (err) {
        // Un fallo de CloudStorage con status de cliente (4xx: tipo no permitido, tamaño,
        // cuota) se refleja tal cual; el resto es 502 (dependencia caída), nunca 500 opaco.
        if (err instanceof CloudStorageUploadError) {
          const status = err.status >= 400 && err.status < 500 ? err.status : 502;
          return reply
            .code(status)
            .send({ code: err.code ?? 'Chat.Attachment.UploadFailed', message: err.detail });
        }
        throw err;
      }
      if (!result.isSuccess) {
        return reply
          .code(attachmentHttpStatus(result.error.code))
          .send({ code: result.error.code, message: result.error.message });
      }
      return reply.code(201).send(result.value);
    },
  );

  // POST /communication/conversations/:id/attachments/:fileId/complete — finaliza
  // la subida (verifica tamano + dispara escaneo).
  app.post(
    '/communication/conversations/:id/attachments/:fileId/complete',
    { preHandler: [app.authenticate] },
    async (request, reply) => {
      const principal = request.principal!;
      const params = AttachmentParams.parse(request.params);
      const result = await completeAttachmentUpload(
        {
          tenantId: principal.tenantId,
          userId: principal.userId,
          conversationId: params.id,
          fileId: params.fileId,
        },
        container,
      );
      if (!result.isSuccess) {
        return reply
          .code(attachmentHttpStatus(result.error.code))
          .send({ code: result.error.code, message: result.error.message });
      }
      return reply.code(202).send(result.value);
    },
  );

  // POST /communication/conversations/:id/attachments/:fileId/download-url — URL
  // presignada, autorizada por membresia y solo si el escaneo lo dejo Available.
  // Communication media el acceso a un blob `ownerType=Communication` que el scope
  // por-dueno de CloudStorage no le entregaria a un CustomerPortal.
  app.post(
    '/communication/conversations/:id/attachments/:fileId/download-url',
    { preHandler: [app.authenticate] },
    async (request, reply) => {
      const principal = request.principal!;
      const params = AttachmentParams.parse(request.params);
      const result = await getAttachmentDownloadUrl(
        {
          tenantId: principal.tenantId,
          userId: principal.userId,
          conversationId: params.id,
          fileId: params.fileId,
        },
        container,
      );
      if (!result.isSuccess) {
        return reply
          .code(attachmentHttpStatus(result.error.code))
          .send({ code: result.error.code, message: result.error.message });
      }
      return reply.send(result.value);
    },
  );
}
