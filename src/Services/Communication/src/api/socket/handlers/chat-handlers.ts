import { randomUUID } from 'node:crypto';
import { logger } from '../../../infrastructure/logger/logger.js';
import { hasPermission, CommunicationPermissions } from '../../../domain/shared/permissions.js';
import type { AppContainer } from '../../../infrastructure/container.js';
import type {
  CommunicationIoServer,
  CommunicationSocket,
} from '../../../infrastructure/socket/build-io.js';
import { SocketRealtimeEmitter } from '../../../infrastructure/socket/socket-realtime-emitter.js';
import { startDirectConversation } from '../../../application/use-cases/start-direct-conversation.js';
import { sendMessage } from '../../../application/use-cases/send-message.js';
import { editMessage } from '../../../application/use-cases/edit-message.js';
import { deleteMessage } from '../../../application/use-cases/delete-message.js';
import { markMessagesRead } from '../../../application/use-cases/mark-messages-read.js';
import {
  ChatSocketEvents,
  DeleteMessagePayloadSchema,
  EditMessagePayloadSchema,
  MarkReadPayloadSchema,
  SendMessagePayloadSchema,
  StartDirectConversationPayloadSchema,
  TypingPayloadSchema,
  type MessageDto,
} from '../../../contracts/socket/chat-socket-events.js';
import type { SocketAck, SocketEnvelope } from '../../../contracts/socket/socket-envelope.js';

/**
 * Registra los handlers de chat en el Socket.IO server. Cada handler:
 *   1. Valida el payload con Zod (nunca confia en el cliente).
 *   2. Comprueba permisos leidos SOLO del JWT verificado (cierra CRIT-18).
 *   3. Llama al use case correspondiente (Result<T> propagado en el ack).
 *   4. Emite el evento server->client con envelope tipado.
 *
 * NO hay logica de negocio aqui — solo boundary + transporte.
 */
export function registerChatHandlers(io: CommunicationIoServer, container: AppContainer): void {
  const emitter = new SocketRealtimeEmitter(io);

  io.on('connection', (socket) => {
    void wireSocket(socket, io, container, emitter);
  });
}

async function wireSocket(
  socket: CommunicationSocket,
  _io: CommunicationIoServer,
  container: AppContainer,
  emitter: SocketRealtimeEmitter,
): Promise<void> {
  const principal = socket.data.principal;
  if (!principal) {
    socket.disconnect(true);
    return;
  }
  const { tenantId, userId } = principal;

  await container.presence.register({
    tenantId,
    userId,
    sessionId: socket.id,
    leaseSeconds: 30,
  });
  const heartbeat = setInterval(() => {
    container.presence
      .heartbeat({ tenantId, userId, sessionId: socket.id, leaseSeconds: 30 })
      .catch((err) => logger.warn({ err }, 'presence heartbeat failed'));
  }, 15_000);

  socket.on(ChatSocketEvents.StartDirectConversation, async (...args: unknown[]) => {
    const raw = args[0];
    const ack = typeof args[1] === 'function' ? (args[1] as (r: SocketAck<{ conversationId: string; wasCreated: boolean }>) => void) : undefined;
    const parsed = StartDirectConversationPayloadSchema.safeParse(raw);
    if (!parsed.success) {
      ack?.({ ok: false, code: 'Chat.BadPayload', message: parsed.error.message });
      return;
    }
    if (!hasPermission(principal.actorType, principal.permissions, CommunicationPermissions.ChatStart)) {
      ack?.({ ok: false, code: 'Auth.Forbidden', message: 'Missing communication.chat.start.' });
      return;
    }
    const result = await startDirectConversation(
      {
        tenantId,
        correlationId: socket.id,
        clientKey: parsed.data.clientKey,
        initiator: { userId, displayName: principal.userId, actorType: principal.actorType },
        // NOTA: el displayName real del destinatario se resolvera en Fase 4 via
        // proyeccion local de usuarios (consumer AuthUserRegistered). Mientras
        // tanto usamos el userId como placeholder — cierra el flujo sin bloquear.
        recipient: { userId: parsed.data.recipientUserId, displayName: parsed.data.recipientUserId, actorType: 'TenantEmployee' },
      },
      container,
    );
    if (!result.isSuccess) {
      ack?.({ ok: false, code: result.error.code, message: result.error.message });
      return;
    }
    await socket.join(`t:${tenantId}:c:${result.value.conversationId}`);
    ack?.({ ok: true, value: result.value });
  });

  socket.on(ChatSocketEvents.SendMessage, async (...args: unknown[]) => {
    const raw = args[0];
    const ack = typeof args[1] === 'function' ? (args[1] as (r: SocketAck<{ message: MessageDto }>) => void) : undefined;
    const parsed = SendMessagePayloadSchema.safeParse(raw);
    if (!parsed.success) {
      ack?.({ ok: false, code: 'Chat.BadPayload', message: parsed.error.message });
      return;
    }
    if (!hasPermission(principal.actorType, principal.permissions, CommunicationPermissions.ChatReply)) {
      ack?.({ ok: false, code: 'Auth.Forbidden', message: 'Missing communication.chat.reply.' });
      return;
    }
    const result = await sendMessage(
      {
        tenantId,
        correlationId: socket.id,
        clientKey: parsed.data.clientKey,
        conversationId: parsed.data.conversationId,
        senderUserId: userId,
        body: parsed.data.body,
        attachmentFileId: parsed.data.attachmentFileId,
        replyToMessageId: parsed.data.replyToMessageId,
      },
      container,
    );
    if (!result.isSuccess) {
      ack?.({ ok: false, code: result.error.code, message: result.error.message });
      return;
    }
    ack?.({ ok: true, value: result.value });
    emitter.emitToConversation({
      tenantId,
      conversationId: parsed.data.conversationId,
      event: ChatSocketEvents.MessageNew,
      envelope: envelope(result.value.message),
    });
  });

  socket.on(ChatSocketEvents.EditMessage, async (...args: unknown[]) => {
    const raw = args[0];
    const ack = typeof args[1] === 'function' ? (args[1] as (r: SocketAck<{ edited: unknown }>) => void) : undefined;
    const parsed = EditMessagePayloadSchema.safeParse(raw);
    if (!parsed.success) {
      ack?.({ ok: false, code: 'Chat.BadPayload', message: parsed.error.message });
      return;
    }
    if (!hasPermission(principal.actorType, principal.permissions, CommunicationPermissions.ChatReply)) {
      ack?.({ ok: false, code: 'Auth.Forbidden', message: 'Missing communication.chat.reply.' });
      return;
    }
    const result = await editMessage(
      {
        tenantId,
        correlationId: socket.id,
        clientKey: parsed.data.clientKey,
        messageId: parsed.data.messageId,
        senderUserId: userId,
        body: parsed.data.body,
      },
      container,
    );
    if (!result.isSuccess) {
      ack?.({ ok: false, code: result.error.code, message: result.error.message });
      return;
    }
    ack?.({ ok: true, value: result.value });
    emitter.emitToConversation({
      tenantId,
      conversationId: result.value.edited.conversationId,
      event: ChatSocketEvents.MessageEdited,
      envelope: envelope(result.value.edited),
    });
  });

  socket.on(ChatSocketEvents.DeleteMessage, async (...args: unknown[]) => {
    const raw = args[0];
    const ack = typeof args[1] === 'function' ? (args[1] as (r: SocketAck<{ deleted: unknown }>) => void) : undefined;
    const parsed = DeleteMessagePayloadSchema.safeParse(raw);
    if (!parsed.success) {
      ack?.({ ok: false, code: 'Chat.BadPayload', message: parsed.error.message });
      return;
    }
    const canModerate = hasPermission(
      principal.actorType,
      principal.permissions,
      CommunicationPermissions.ChatModerate,
    );
    const result = await deleteMessage(
      {
        tenantId,
        correlationId: socket.id,
        clientKey: parsed.data.clientKey,
        messageId: parsed.data.messageId,
        actorUserId: userId,
        actorCanModerate: canModerate,
      },
      container,
    );
    if (!result.isSuccess) {
      ack?.({ ok: false, code: result.error.code, message: result.error.message });
      return;
    }
    ack?.({ ok: true, value: result.value });
    emitter.emitToConversation({
      tenantId,
      conversationId: result.value.deleted.conversationId,
      event: ChatSocketEvents.MessageDeleted,
      envelope: envelope(result.value.deleted),
    });
  });

  socket.on(ChatSocketEvents.MarkRead, async (...args: unknown[]) => {
    const raw = args[0];
    const ack = typeof args[1] === 'function' ? (args[1] as (r: SocketAck<{ markedCount: number }>) => void) : undefined;
    const parsed = MarkReadPayloadSchema.safeParse(raw);
    if (!parsed.success) {
      ack?.({ ok: false, code: 'Chat.BadPayload', message: parsed.error.message });
      return;
    }
    const result = await markMessagesRead(
      {
        tenantId,
        conversationId: parsed.data.conversationId,
        userUserId: userId,
        lastReadMessageId: parsed.data.lastReadMessageId,
      },
      container,
    );
    if (!result.isSuccess) {
      ack?.({ ok: false, code: result.error.code, message: result.error.message });
      return;
    }
    ack?.({ ok: true, value: { markedCount: result.value.markedCount } });
    emitter.emitToConversation({
      tenantId,
      conversationId: parsed.data.conversationId,
      event: ChatSocketEvents.MessageRead,
      envelope: envelope(result.value.receipt),
    });
  });

  socket.on(ChatSocketEvents.TypingStart, (...args: unknown[]) => {
    const parsed = TypingPayloadSchema.safeParse(args[0]);
    if (!parsed.success) return;
    emitter.emitToConversation({
      tenantId,
      conversationId: parsed.data.conversationId,
      event: ChatSocketEvents.TypingStarted,
      envelope: envelope({
        conversationId: parsed.data.conversationId,
        userId,
        displayName: principal.userId,
      }),
    });
  });

  socket.on(ChatSocketEvents.TypingStop, (...args: unknown[]) => {
    const parsed = TypingPayloadSchema.safeParse(args[0]);
    if (!parsed.success) return;
    emitter.emitToConversation({
      tenantId,
      conversationId: parsed.data.conversationId,
      event: ChatSocketEvents.TypingStopped,
      envelope: envelope({
        conversationId: parsed.data.conversationId,
        userId,
        displayName: principal.userId,
      }),
    });
  });

  socket.on('disconnect', () => {
    clearInterval(heartbeat);
    container.presence
      .unregister({ tenantId, userId, sessionId: socket.id })
      .catch((err) => logger.warn({ err }, 'presence unregister failed'));
  });
}

function envelope<T>(payload: T): SocketEnvelope<T> {
  return {
    eventId: randomUUID(),
    correlationId: '',
    emittedAtUtc: new Date().toISOString(),
    payload,
  };
}
