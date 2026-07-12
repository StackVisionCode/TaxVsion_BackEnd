import { randomUUID } from 'node:crypto';
import { logger } from '../../../infrastructure/logger/logger.js';
import { hasPermission, CommunicationPermissions } from '../../../domain/shared/permissions.js';
import type { AppContainer } from '../../../infrastructure/container.js';
import type {
  CommunicationIoServer,
  CommunicationSocket,
} from '../../../infrastructure/socket/build-io.js';
import { SocketRealtimeEmitter } from '../../../infrastructure/socket/socket-realtime-emitter.js';
import { resolveDisplayName } from './resolve-display-name.js';
import { startDirectConversation } from '../../../application/use-cases/start-direct-conversation.js';
import { startGroupConversation } from '../../../application/use-cases/start-group-conversation.js';
import { addGroupParticipant } from '../../../application/use-cases/add-group-participant.js';
import { removeGroupParticipant } from '../../../application/use-cases/remove-group-participant.js';
import { sendMessage } from '../../../application/use-cases/send-message.js';
import { editMessage } from '../../../application/use-cases/edit-message.js';
import { deleteMessage } from '../../../application/use-cases/delete-message.js';
import { markMessagesRead } from '../../../application/use-cases/mark-messages-read.js';
import {
  ChatSocketEvents,
  AddGroupParticipantPayloadSchema,
  DeleteMessagePayloadSchema,
  EditMessagePayloadSchema,
  MarkReadPayloadSchema,
  RemoveGroupParticipantPayloadSchema,
  SendMessagePayloadSchema,
  StartDirectConversationPayloadSchema,
  StartGroupConversationPayloadSchema,
  TypingPayloadSchema,
  type ConversationCreatedDto,
  type ConversationParticipantAddedDto,
  type ConversationParticipantRemovedDto,
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

/**
 * TTL del indicador "escribiendo...". Si el cliente nunca manda TypingStop
 * (crash, cierre abrupto de pestana sin disconnect todavia procesado, etc.),
 * el servidor lo auto-expira para que el indicador no quede pegado en los
 * peers. Timer por-instancia (no Redis): el emit ya viaja por el adapter de
 * Socket.IO, asi que no importa en que instancia vive el timer.
 */
const TYPING_TIMEOUT_MS = 8_000;
const typingTimers = new Map<string, { timer: NodeJS.Timeout; conversationId: string }>();

function typingKey(tenantId: string, conversationId: string, userId: string): string {
  return `${tenantId}:${conversationId}:${userId}`;
}

function clearTypingTimer(key: string): void {
  const entry = typingTimers.get(key);
  if (!entry) return;
  clearTimeout(entry.timer);
  typingTimers.delete(key);
}

async function wireSocket(
  socket: CommunicationSocket,
  io: CommunicationIoServer,
  container: AppContainer,
  emitter: SocketRealtimeEmitter,
): Promise<void> {
  const principal = socket.data.principal;
  if (!principal) {
    socket.disconnect(true);
    return;
  }
  const { tenantId, userId } = principal;
  // Resuelto una vez por conexion (no por mensaje/typing-event) — un socket
  // vive minutos/horas, el displayName del propio usuario no cambia a mitad
  // de sesion en la practica.
  const selfDisplayName = await resolveDisplayName(container.userDirectory, userId);

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
    const recipientDisplayName = await resolveDisplayName(container.userDirectory, parsed.data.recipientUserId);
    const result = await startDirectConversation(
      {
        tenantId,
        correlationId: socket.id,
        clientKey: parsed.data.clientKey,
        initiator: { userId, displayName: selfDisplayName, actorType: principal.actorType },
        recipient: { userId: parsed.data.recipientUserId, displayName: recipientDisplayName, actorType: 'TenantEmployee' },
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

  socket.on(ChatSocketEvents.StartGroupConversation, async (...args: unknown[]) => {
    const raw = args[0];
    const ack = typeof args[1] === 'function' ? (args[1] as (r: SocketAck<{ conversationId: string }>) => void) : undefined;
    const parsed = StartGroupConversationPayloadSchema.safeParse(raw);
    if (!parsed.success) {
      ack?.({ ok: false, code: 'Chat.BadPayload', message: parsed.error.message });
      return;
    }
    if (!hasPermission(principal.actorType, principal.permissions, CommunicationPermissions.GroupCreate)) {
      ack?.({ ok: false, code: 'Auth.Forbidden', message: 'Missing communication.group.create.' });
      return;
    }
    const members = await Promise.all(
      parsed.data.memberUserIds.map(async (memberUserId) => ({
        userId: memberUserId,
        displayName: await resolveDisplayName(container.userDirectory, memberUserId),
        actorType: 'TenantEmployee',
      })),
    );
    const result = await startGroupConversation(
      {
        tenantId,
        correlationId: socket.id,
        clientKey: parsed.data.clientKey,
        creator: { userId, displayName: selfDisplayName, actorType: principal.actorType },
        title: parsed.data.title,
        members,
      },
      container,
    );
    if (!result.isSuccess) {
      ack?.({ ok: false, code: result.error.code, message: result.error.message });
      return;
    }
    await socket.join(`t:${tenantId}:c:${result.value.conversationId}`);
    ack?.({ ok: true, value: result.value });

    // Los miembros no estan conectados a la room todavia (no llamaron
    // socket.join) — se les notifica por su room personal para que el
    // cliente pueda refrescar su lista de conversaciones / unirse.
    const createdDto: ConversationCreatedDto = {
      id: result.value.conversationId,
      kind: 'Group',
      title: parsed.data.title,
      createdByUserId: userId,
      createdAtUtc: new Date().toISOString(),
    };
    for (const member of members) {
      emitter.emitToUser({
        tenantId,
        userId: member.userId,
        event: ChatSocketEvents.ConversationCreated,
        envelope: envelope(createdDto),
      });
    }
  });

  socket.on(ChatSocketEvents.AddGroupParticipant, async (...args: unknown[]) => {
    const raw = args[0];
    const ack = typeof args[1] === 'function' ? (args[1] as (r: SocketAck<{ conversationId: string; newParticipantUserId: string }>) => void) : undefined;
    const parsed = AddGroupParticipantPayloadSchema.safeParse(raw);
    if (!parsed.success) {
      ack?.({ ok: false, code: 'Chat.BadPayload', message: parsed.error.message });
      return;
    }
    if (!hasPermission(principal.actorType, principal.permissions, CommunicationPermissions.GroupManageMembers)) {
      ack?.({ ok: false, code: 'Auth.Forbidden', message: 'Missing communication.group.manage_members.' });
      return;
    }
    const newMemberDisplayName = await resolveDisplayName(container.userDirectory, parsed.data.newMemberUserId);
    const result = await addGroupParticipant(
      {
        tenantId,
        correlationId: socket.id,
        clientKey: parsed.data.clientKey,
        conversationId: parsed.data.conversationId,
        actorUserId: userId,
        newMember: { userId: parsed.data.newMemberUserId, displayName: newMemberDisplayName, actorType: 'TenantEmployee' },
      },
      container,
    );
    if (!result.isSuccess) {
      ack?.({ ok: false, code: result.error.code, message: result.error.message });
      return;
    }
    ack?.({ ok: true, value: result.value });
    const addedDto: ConversationParticipantAddedDto = {
      conversationId: result.value.conversationId,
      addedByUserId: userId,
      newParticipantUserId: result.value.newParticipantUserId,
      newParticipantDisplayName: newMemberDisplayName,
      addedAtUtc: new Date().toISOString(),
    };
    emitter.emitToConversation({
      tenantId,
      conversationId: result.value.conversationId,
      event: ChatSocketEvents.ConversationParticipantAdded,
      envelope: envelope(addedDto),
    });
    // El nuevo miembro tampoco esta en la room (no llamo socket.join) — se le
    // avisa por su room personal, igual que al crear el grupo.
    emitter.emitToUser({
      tenantId,
      userId: result.value.newParticipantUserId,
      event: ChatSocketEvents.ConversationParticipantAdded,
      envelope: envelope(addedDto),
    });
  });

  socket.on(ChatSocketEvents.RemoveGroupParticipant, async (...args: unknown[]) => {
    const raw = args[0];
    const ack = typeof args[1] === 'function' ? (args[1] as (r: SocketAck<{ conversationId: string; removedParticipantUserId: string; reason: 'Left' | 'Kicked' }>) => void) : undefined;
    const parsed = RemoveGroupParticipantPayloadSchema.safeParse(raw);
    if (!parsed.success) {
      ack?.({ ok: false, code: 'Chat.BadPayload', message: parsed.error.message });
      return;
    }
    const targetUserId = parsed.data.targetUserId ?? userId;
    // Salir de un grupo (targetUserId === self) no requiere el permiso de
    // gestion — solo expulsar a otro participante lo requiere.
    if (targetUserId !== userId && !hasPermission(principal.actorType, principal.permissions, CommunicationPermissions.GroupManageMembers)) {
      ack?.({ ok: false, code: 'Auth.Forbidden', message: 'Missing communication.group.manage_members.' });
      return;
    }
    const result = await removeGroupParticipant(
      {
        tenantId,
        correlationId: socket.id,
        clientKey: parsed.data.clientKey,
        conversationId: parsed.data.conversationId,
        actorUserId: userId,
        targetUserId,
      },
      container,
    );
    if (!result.isSuccess) {
      ack?.({ ok: false, code: result.error.code, message: result.error.message });
      return;
    }
    ack?.({ ok: true, value: result.value });
    const removedDto: ConversationParticipantRemovedDto = {
      conversationId: result.value.conversationId,
      removedByUserId: userId,
      removedParticipantUserId: result.value.removedParticipantUserId,
      reason: result.value.reason,
      removedAtUtc: new Date().toISOString(),
    };
    emitter.emitToConversation({
      tenantId,
      conversationId: result.value.conversationId,
      event: ChatSocketEvents.ConversationParticipantRemoved,
      envelope: envelope(removedDto),
    });
    const room = `t:${tenantId}:c:${result.value.conversationId}`;
    if (targetUserId === userId) {
      await socket.leave(room);
    } else {
      // El expulsado puede tener otros sockets conectados (otra pestana,
      // otro dispositivo) — hay que sacarlos a todos de la room o seguirian
      // recibiendo mensajes de un grupo del que ya no son parte.
      io.in(`t:${tenantId}:u:${targetUserId}`).socketsLeave(room);
    }
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
    const allowed = await container.rateLimiter.allow({
      scope: 'chat.send',
      tenantId,
      userId,
      maxPerWindow: 30,
      windowSeconds: 10,
    });
    if (!allowed) {
      ack?.({ ok: false, code: 'Chat.RateLimited', message: 'Too many messages, slow down.' });
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
    const allowed = await container.rateLimiter.allow({
      scope: 'chat.edit',
      tenantId,
      userId,
      maxPerWindow: 20,
      windowSeconds: 10,
    });
    if (!allowed) {
      ack?.({ ok: false, code: 'Chat.RateLimited', message: 'Too many edits, slow down.' });
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

  const emitTypingStopped = (conversationId: string): void => {
    emitter.emitToConversation({
      tenantId,
      conversationId,
      event: ChatSocketEvents.TypingStopped,
      envelope: envelope({ conversationId, userId, displayName: selfDisplayName }),
    });
  };

  socket.on(ChatSocketEvents.TypingStart, async (...args: unknown[]) => {
    const parsed = TypingPayloadSchema.safeParse(args[0]);
    if (!parsed.success) return;
    const allowed = await container.rateLimiter.allow({
      scope: 'chat.typing',
      tenantId,
      userId,
      maxPerWindow: 20,
      windowSeconds: 10,
    });
    if (!allowed) return;
    const { conversationId } = parsed.data;
    emitter.emitToConversation({
      tenantId,
      conversationId,
      event: ChatSocketEvents.TypingStarted,
      envelope: envelope({ conversationId, userId, displayName: selfDisplayName }),
    });

    const key = typingKey(tenantId, conversationId, userId);
    clearTypingTimer(key);
    const timer = setTimeout(() => {
      typingTimers.delete(key);
      emitTypingStopped(conversationId);
    }, TYPING_TIMEOUT_MS);
    typingTimers.set(key, { timer, conversationId });
  });

  socket.on(ChatSocketEvents.TypingStop, (...args: unknown[]) => {
    const parsed = TypingPayloadSchema.safeParse(args[0]);
    if (!parsed.success) return;
    clearTypingTimer(typingKey(tenantId, parsed.data.conversationId, userId));
    emitTypingStopped(parsed.data.conversationId);
  });

  socket.on('disconnect', () => {
    clearInterval(heartbeat);
    container.presence
      .unregister({ tenantId, userId, sessionId: socket.id })
      .catch((err) => logger.warn({ err }, 'presence unregister failed'));

    // Cierra cualquier indicador de "escribiendo..." que este socket dejo
    // pendiente — sin esto, un cierre abrupto de pestana deja el indicador
    // pegado hasta el timeout de 8s (que igual dispara, pero mejor limpiar ya).
    for (const [key, entry] of typingTimers) {
      if (!key.startsWith(`${tenantId}:`) || !key.endsWith(`:${userId}`)) continue;
      clearTypingTimer(key);
      emitTypingStopped(entry.conversationId);
    }
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
