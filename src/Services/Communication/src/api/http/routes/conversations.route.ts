import type { FastifyInstance } from 'fastify';
import { z } from 'zod';
import { listConversations } from '../../../application/use-cases/list-conversations.js';
import { getMessages } from '../../../application/use-cases/get-messages.js';
import { markMessagesRead } from '../../../application/use-cases/mark-messages-read.js';
import { searchMessages } from '../../../application/use-cases/search-messages.js';
import { getAttachmentMetadata } from '../../../application/use-cases/get-attachment-metadata.js';
import { getAttachmentDownloadUrl } from '../../../application/use-cases/get-attachment-download-url.js';
import { requestVoiceNoteTranscript } from '../../../application/use-cases/request-voice-note-transcript.js';
import { createAttachmentUpload } from '../../../application/use-cases/create-attachment-upload.js';
import { completeAttachmentUpload } from '../../../application/use-cases/complete-attachment-upload.js';
import { CloudStorageUploadError } from '../../../infrastructure/cloudstorage/http-cloudstorage-upload-client.js';
import { CommunicationPermissions, checkPermission } from '../../../domain/shared/permissions.js';
import { CONTENT_PAGE_MAX, listConversationContent } from '../../../application/use-cases/list-conversation-content.js';
import { listConversationAudit } from '../../../application/use-cases/list-conversation-audit.js';
import { completeGroupAvatarUpload, createGroupAvatarUpload } from '../../../application/use-cases/group-avatar-upload.js';
import { isTenantAdminActor } from '../../../application/use-cases/group-membership-policy.js';
import { requirePermission } from '../plugins/require-permission.js';
import { listMessageEdits } from '../../../application/use-cases/list-message-edits.js';
import { OptionalBooleanQuery } from '../schemas/strict-boolean-query.js';
import type { AppContainer } from '../../../infrastructure/container.js';

const MessageTranscriptParams = z.object({ id: z.string().uuid(), messageId: z.string().uuid() });
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

const KindSchema = z.enum(['Direct', 'Group', 'Support', 'Meeting']);
const ListQuerySchema = z.object({
  page: z.coerce.number().int().min(1).default(1),
  size: z.coerce.number().int().min(1).max(100).default(20),
  includeArchived: OptionalBooleanQuery,
  // F8a: `kinds=Direct,Group` (Chat), `kinds=Support` (módulo Support); sin `kinds` = todo (compatibilidad).
  kinds: z
    .string()
    .optional()
    .transform((raw) => (raw ? raw.split(',').map((k) => k.trim()).filter(Boolean) : undefined))
    .pipe(z.array(KindSchema).max(4).optional()),
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
  app.get(
    '/communication/conversations',
    {
      preHandler: [app.authenticate, requirePermission(container, CommunicationPermissions.ChatReply)],
    },
    async (request, reply) => {
      const principal = request.principal!;
      const query = ListQuerySchema.parse(request.query);
      const result = await listConversations(
        {
          tenantId: principal.tenantId,
          userId: principal.userId,
          page: query.page,
          size: query.size,
          includeArchived: query.includeArchived ?? false,
          ...(query.kinds ? { kinds: query.kinds } : {}),
        },
        container,
      );
      if (!result.isSuccess) {
        return reply.code(400).send({ code: result.error.code, message: result.error.message });
      }
      return reply.send(result.value);
    },
  );

  // GET /communication/conversations/:id/messages
  app.get(
    '/communication/conversations/:id/messages',
    {
      preHandler: [app.authenticate, requirePermission(container, CommunicationPermissions.ChatReply)],
    },
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
    {
      preHandler: [app.authenticate, requirePermission(container, CommunicationPermissions.ChatReply)],
    },
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

  // GET /communication/conversations/:id/content?tab=Media|Documents|Audio|Links&before&take — F8d: pestañas de Info.
  app.get(
    '/communication/conversations/:id/content',
    { preHandler: [app.authenticate, requirePermission(container, CommunicationPermissions.ChatReply)] },
    async (request, reply) => {
      const principal = request.principal;
      if (!principal) return reply.code(401).send({ code: 'Auth.Unauthenticated', message: 'Authentication required.' });
      const params = ConversationParam.parse(request.params);
      const query = z
        .object({
          tab: z.enum(['Media', 'Documents', 'Audio', 'Links']),
          before: z.string().max(128).optional(),
          take: z.coerce.number().int().min(1).max(CONTENT_PAGE_MAX).optional(),
        })
        .parse(request.query);
      const result = await listConversationContent(
        {
          tenantId: principal.tenantId,
          conversationId: params.id,
          requesterUserId: principal.userId,
          tab: query.tab,
          before: query.before ?? null,
          ...(query.take !== undefined ? { take: query.take } : {}),
        },
        container,
      );
      if (!result.isSuccess) {
        const status = result.error.code === 'Chat.Conversation.NotParticipant' ? 403 : 404;
        return reply.code(status).send({ code: result.error.code, message: result.error.message });
      }
      return reply.send(result.value);
    },
  );

  // GET /communication/conversations/:id/messages/search — Fase Backend 9.
  // Query LIKE-based (no Full-Text catalog en el entorno actual, ver
  // docblock en searchMessages y en PrismaMessageRepository.searchByBody).
  app.get(
    '/communication/conversations/:id/messages/search',
    {
      preHandler: [app.authenticate, requirePermission(container, CommunicationPermissions.ChatReply)],
    },
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
    {
      preHandler: [app.authenticate, requirePermission(container, CommunicationPermissions.ChatReply)],
    },
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
    {
      preHandler: [app.authenticate, requirePermission(container, CommunicationPermissions.ScreenshotCreate)],
    },
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

  /** D-22: superpoder para administrar grupos sin ser miembro (Tenant Admin o `group.manage_members`). */
  const groupSuperpower = async (principal: Parameters<typeof checkPermission>[0]): Promise<boolean> =>
    isTenantAdminActor(principal.actorType) ||
    (await checkPermission(principal, CommunicationPermissions.GroupManageMembers, container.userPermissions)).allowed;

  // GET /communication/conversations/:id/audit — F8b (T-22): bitácora del grupo para quien lo administra.
  app.get(
    '/communication/conversations/:id/audit',
    { preHandler: [app.authenticate, requirePermission(container, CommunicationPermissions.ChatReply)] },
    async (request, reply) => {
      const principal = request.principal;
      if (!principal) return reply.code(401).send({ code: 'Auth.Unauthenticated', message: 'Authentication required.' });
      const params = z.object({ id: z.string().uuid() }).parse(request.params);
      const query = z
        .object({ take: z.coerce.number().int().min(1).max(100).default(50), before: z.string().datetime().optional() })
        .parse(request.query);
      const canAdministerAnyGroup = await groupSuperpower(principal);
      const result = await listConversationAudit(
        {
          tenantId: principal.tenantId,
          conversationId: params.id,
          requesterUserId: principal.userId,
          canAdministerAnyGroup,
          take: query.take,
          ...(query.before ? { beforeUtc: query.before } : {}),
        },
        container,
      );
      if (!result.isSuccess) {
        const status = result.error.code === 'Chat.Group.AdminOnly' ? 403 : 404;
        return reply.code(status).send({ code: result.error.code, message: result.error.message });
      }
      return reply.send(result.value);
    },
  );

  // GET /communication/conversations/:id/messages/:messageId/edits — F8: versiones anteriores de un mensaje editado.
  app.get(
    '/communication/conversations/:id/messages/:messageId/edits',
    {
      preHandler: [app.authenticate, requirePermission(container, CommunicationPermissions.ChatReply)],
    },
    async (request, reply) => {
      const principal = request.principal;
      if (!principal) return reply.code(401).send({ code: 'Auth.Unauthenticated', message: 'Authentication required.' });
      const params = MessageTranscriptParams.parse(request.params);
      const result = await listMessageEdits(
        { tenantId: principal.tenantId, conversationId: params.id, messageId: params.messageId, requesterUserId: principal.userId },
        container,
      );
      if (!result.isSuccess) {
        const status = result.error.code === 'Chat.Conversation.NotParticipant' ? 403 : 404;
        return reply.code(status).send({ code: result.error.code, message: result.error.message });
      }
      return reply.send(result.value);
    },
  );

  // POST /communication/conversations/:id/messages/:messageId/transcript — F7: transcribe una nota de voz
  // a demanda (boton "Transcribe"); 202 con el estado. El texto llega por `chat.message.transcript_updated`.
  app.post(
    '/communication/conversations/:id/messages/:messageId/transcript',
    {
      preHandler: [app.authenticate, requirePermission(container, CommunicationPermissions.ChatReply)],
    },
    async (request, reply) => {
      const principal = request.principal;
      if (!principal) return reply.code(401).send({ code: 'Auth.Unauthenticated', message: 'Authentication required.' });
      const params = MessageTranscriptParams.parse(request.params);
      const result = await requestVoiceNoteTranscript(
        {
          tenantId: principal.tenantId,
          userId: principal.userId,
          conversationId: params.id,
          messageId: params.messageId,
          correlationId: request.id,
        },
        { ...container, emitter: container.emitter },
      );
      if (!result.isSuccess) {
        const status =
          result.error.code === 'Chat.Message.NotFound' ? 404 : result.error.code === 'Chat.Conversation.NotParticipant' ? 403 : 409;
        return reply.code(status).send({ code: result.error.code, message: result.error.message });
      }
      return reply.code(202).send(result.value);
    },
  );

  // POST /communication/conversations/:id/avatar/upload — F8b (T-21): foto del grupo, subida mediada a `Avatars`.
  app.post(
    '/communication/conversations/:id/avatar/upload',
    { preHandler: [app.authenticate, requirePermission(container, CommunicationPermissions.ChatReply)] },
    async (request, reply) => {
      const principal = request.principal;
      if (!principal) return reply.code(401).send({ code: 'Auth.Unauthenticated', message: 'Authentication required.' });
      const params = ConversationParam.parse(request.params);
      const body = CreateUploadBody.parse(request.body);
      try {
        const result = await createGroupAvatarUpload(
          {
            tenantId: principal.tenantId,
            userId: principal.userId,
            conversationId: params.id,
            canAdministerAnyGroup: await groupSuperpower(principal),
            originalName: body.originalName,
            contentType: body.contentType,
            sizeBytes: body.sizeBytes,
          },
          container,
        );
        if (!result.isSuccess) return reply.code(groupAvatarHttpStatus(result.error.code)).send({ code: result.error.code, message: result.error.message });
        return reply.send(result.value);
      } catch (err) {
        if (err instanceof CloudStorageUploadError) {
          const status = err.status >= 400 && err.status < 500 ? err.status : 502;
          return reply.code(status).send({ code: err.code ?? 'Chat.Group.AvatarUploadFailed', message: "Couldn't upload the photo." });
        }
        throw err;
      }
    },
  );

  app.post(
    '/communication/conversations/:id/avatar/:fileId/complete',
    { preHandler: [app.authenticate, requirePermission(container, CommunicationPermissions.ChatReply)] },
    async (request, reply) => {
      const principal = request.principal;
      if (!principal) return reply.code(401).send({ code: 'Auth.Unauthenticated', message: 'Authentication required.' });
      const params = AttachmentParams.parse(request.params);
      const result = await completeGroupAvatarUpload(
        {
          tenantId: principal.tenantId,
          userId: principal.userId,
          conversationId: params.id,
          canAdministerAnyGroup: await groupSuperpower(principal),
          fileId: params.fileId,
        },
        container,
      );
      if (!result.isSuccess) return reply.code(groupAvatarHttpStatus(result.error.code)).send({ code: result.error.code, message: result.error.message });
      return reply.code(202).send(result.value);
    },
  );

  // POST /communication/conversations/:id/attachments/:fileId/complete — finaliza
  // la subida (verifica tamano + dispara escaneo).
  app.post(
    '/communication/conversations/:id/attachments/:fileId/complete',
    {
      preHandler: [app.authenticate, requirePermission(container, CommunicationPermissions.ScreenshotCreate)],
    },
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
    {
      preHandler: [app.authenticate, requirePermission(container, CommunicationPermissions.ChatReply)],
    },
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

/** F8b: estado HTTP de los errores de la foto del grupo. */
function groupAvatarHttpStatus(code: string): number {
  if (code === 'Chat.Conversation.NotFound') return 404;
  if (code === 'Chat.Group.AdminOnly') return 403;
  if (code === 'Chat.Group.AvatarType') return 415;
  return 409;
}
