import { randomUUID } from 'node:crypto';
import { config } from '../../../infrastructure/config.js';
import { logger } from '../../../infrastructure/logger/logger.js';
import {
  checkPermission,
  CommunicationPermissions,
  isPlatformAdmin as isPlatformAdminActorType,
} from '../../../domain/shared/permissions.js';
import type { AppContainer } from '../../../infrastructure/container.js';
import type {
  CommunicationIoServer,
  CommunicationSocket,
} from '../../../infrastructure/socket/build-io.js';
import { SocketRealtimeEmitter } from '../../../infrastructure/socket/socket-realtime-emitter.js';
import { resolveDisplayName } from './resolve-display-name.js';
import { resolveActorType } from './resolve-actor-type.js';
import { CommunicationRateLimitPolicyNames } from '../../../domain/rate-limit/rate-limit-policies.js';
import { startDirectConversation } from '../../../application/use-cases/start-direct-conversation.js';
import { startGroupConversation } from '../../../application/use-cases/start-group-conversation.js';
import { addGroupParticipant } from '../../../application/use-cases/add-group-participant.js';
import { removeGroupParticipant } from '../../../application/use-cases/remove-group-participant.js';
import { sendMessage } from '../../../application/use-cases/send-message.js';
import { editMessage } from '../../../application/use-cases/edit-message.js';
import { deleteMessage } from '../../../application/use-cases/delete-message.js';
import { markMessagesRead } from '../../../application/use-cases/mark-messages-read.js';
import { markMessagesDelivered } from '../../../application/use-cases/mark-messages-delivered.js';
import { markConnectedDelivered } from '../../../application/use-cases/mark-connected-delivered.js';
import { markLiveDelivered } from '../../../application/use-cases/mark-live-delivered.js';
import { sendSupportAgentMessage } from '../../../application/use-cases/send-support-agent-message.js';
import { addMessageReaction } from '../../../application/use-cases/add-message-reaction.js';
import { removeMessageReaction } from '../../../application/use-cases/remove-message-reaction.js';
import { pinMessage } from '../../../application/use-cases/pin-message.js';
import { unpinMessage } from '../../../application/use-cases/unpin-message.js';
import { forwardMessage } from '../../../application/use-cases/forward-message.js';
import { resolveAvatars } from '../../../application/use-cases/resolve-avatars.js';
import { toConversationListItem, type ConversationListItem } from '../../../application/use-cases/list-conversations.js';
import { isTenantAdminActor } from '../../../application/use-cases/group-membership-policy.js';
import {
  setConversationMuted,
  setGroupAdminsOnly,
  setGroupMemberRole,
  transferGroupOwnership,
  updateGroupInfo,
  type GroupActor,
} from '../../../application/use-cases/group-administration.js';
import type { Result } from '../../../domain/shared/result.js';
import {
  ChatSocketEvents,
  AddGroupParticipantPayloadSchema,
  AddReactionPayloadSchema,
  DeleteMessagePayloadSchema,
  EditMessagePayloadSchema,
  ForwardMessagePayloadSchema,
  MarkReadPayloadSchema,
  MarkDeliveredPayloadSchema,
  PinMessagePayloadSchema,
  RemoveGroupParticipantPayloadSchema,
  RemoveReactionPayloadSchema,
  SetConversationMutedPayloadSchema,
  SetGroupAdminsOnlyPayloadSchema,
  SetGroupMemberRolePayloadSchema,
  TransferGroupOwnershipPayloadSchema,
  UpdateGroupInfoPayloadSchema,
  SendMessagePayloadSchema,
  StartDirectConversationPayloadSchema,
  StartGroupConversationPayloadSchema,
  SupportAgentJoinPayloadSchema,
  SupportAgentSendMessagePayloadSchema,
  TypingPayloadSchema,
  RecordingPayloadSchema,
  PresenceQueryPayloadSchema,
  UnpinMessagePayloadSchema,
  type ConversationCreatedDto,
  type ConversationParticipantAddedDto,
  type ConversationParticipantRemovedDto,
  type MessageDto,
} from '../../../contracts/socket/chat-socket-events.js';
import type { SocketAck, SocketEnvelope } from '../../../contracts/socket/socket-envelope.js';

// Detrás de cloudflared el WS cae y reconecta en ~0.5s. Si des-registramos la
// presencia al instante, cada microcaída publica Offline y luego Online (parpadeo:
// "nunca ambos online"). Esperamos esta gracia antes de dar de baja la sesión: si
// el usuario ya reconectó (otra sesión con heartbeat vivo), su presencia sigue
// arriba y unregister de la sesión vieja no baja el estado; si se fue de verdad,
// publica Offline con este pequeño retraso. Solo tiene que cubrir la ventana de
// reconexión (~0.5s), así que 2s deja Offline casi instantáneo sin reintroducir el
// parpadeo. Debe ser < leaseSeconds (30s).
const PRESENCE_DISCONNECT_GRACE_MS = 2_000;

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
const typingTimers = new Map<string, { timer: NodeJS.Timeout; conversationTenantId: string; conversationId: string }>();

/**
 * TTL del indicador "grabando una nota de voz…". El cliente re-emite RecordingStart cada ~12-15s
 * mientras graba (una grabacion puede durar minutos), renovando este TTL — igual que typing se renueva
 * por tecleo. Si el cliente crashea, el disconnect lo limpia; este timer es la red de seguridad si el
 * RecordingStop nunca llega. Timer por-instancia (el emit viaja por el adapter de Socket.IO).
 */
const RECORDING_TIMEOUT_MS = 20_000;
const recordingTimers = new Map<string, { timer: NodeJS.Timeout; conversationTenantId: string; conversationId: string }>();

function typingKey(tenantId: string, conversationId: string, userId: string): string {
  return `${tenantId}:${conversationId}:${userId}`;
}

function clearTypingTimer(key: string): void {
  const entry = typingTimers.get(key);
  if (!entry) return;
  clearTimeout(entry.timer);
  typingTimers.delete(key);
}

function clearRecordingTimer(key: string): void {
  const entry = recordingTimers.get(key);
  if (!entry) return;
  clearTimeout(entry.timer);
  recordingTimers.delete(key);
}

/** Tope de conversaciones a las que se une un socket en connect. Un usuario real tiene decenas;
 *  el tope acota el trabajo de un usuario con muchísimas (las rooms son baratas, pero no ilimitadas). */
const MAX_JOINED_CONVERSATIONS = 500;

/**
 * ¿Este socket es parte de la conversación? La membresía ya la resuelve el join-on-connect (el socket
 * entra SOLO a sus propias rooms) y el alta al crear o entrar a una nueva, así que preguntarle a la room
 * es la misma verdad que usa la entrega, sin una consulta extra. Se exporta para poder probarla: sin
 * este chequeo, cualquiera emitía "está escribiendo…" en la conversación de otros, con su nombre visible.
 */
export function isMemberOfConversation(
  rooms: ReadonlySet<string>,
  tenantId: string,
  conversationId: string,
): boolean {
  return rooms.has(`t:${tenantId}:c:${conversationId}`);
}

export function resolveJoinedConversationTenant(
  rooms: ReadonlySet<string>,
  actorTenantId: string,
  conversationId: string,
): string | null {
  if (isMemberOfConversation(rooms, actorTenantId, conversationId)) {
    return actorTenantId;
  }

  const suffix = `:c:${conversationId}`;
  for (const room of rooms) {
    if (room.startsWith('t:') && room.endsWith(suffix)) {
      return room.slice(2, -suffix.length);
    }
  }

  return null;
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

  // Join-on-connect: los mensajes/typing se entregan SOLO a la room de conversación
  // (emitToConversation → `t:{tenant}:c:{id}`). Sin esto, un socket solo entraba a esa room al
  // ejecutar start_direct/start_group, así que el RECEPTOR de un directo (y los miembros de un grupo
  // que no lo crearon) nunca la recibía en vivo, y en cada reconexión se perdía la membresía
  // (connectionStateRecovery es best-effort, no un sustituto). Uniéndolo a TODAS sus conversaciones
  // en cada (re)connect, la entrega es determinista e independiente de quién inició o de la recuperación.
  try {
    const own = await container.conversations.listForUser({
      tenantId,
      userId,
      take: MAX_JOINED_CONVERSATIONS,
      skip: 0,
      includeArchived: false,
    });
    if (own.length > 0) {
      await socket.join(own.map((c) => `t:${tenantId}:c:${c.id}`));

      // "Conectado = entregado (2 grises)": marca delivered el backlog entrante que llegó ANTES de que
      // este socket estuviera vivo (el camino en vivo solo cubre lo que llega por evento con la conv
      // cerrada). Sin esto, el receptor online-pero-sin-abrir dejaba los mensajes en "enviado" (1
      // cotejo) y al ABRIR saltaban directo a leído — la regla intermedia nunca se veía. Best-effort.
      try {
        const receipts = await markConnectedDelivered(
          { tenantId, userUserId: userId, conversationIds: own.map((c) => c.id) },
          container,
        );
        for (const receipt of receipts) {
          emitter.emitToConversation({
            tenantId,
            conversationId: receipt.conversationId,
            event: ChatSocketEvents.MessageDelivered,
            envelope: envelope(receipt),
          });
        }
      } catch (err) {
        logger.warn({ err, userId }, 'mark-connected-delivered: no se pudo marcar el backlog entregado');
      }
    }
  } catch (err) {
    // Best-effort: si falla, el socket sigue vivo (rooms de tenant/usuario ya unidas en el middleware);
    // un start_direct posterior o la próxima reconexión reintentan.
    logger.warn({ err, userId }, 'join-on-connect: no se pudieron unir las rooms de conversación');
  }

  // Support agent join-on-connect: si el usuario es agente del tenant Platform, unirlo a las salas de
  // sus tickets ASIGNADOS (no terminales) para recibir el chat en vivo sin tener que abrir cada ticket.
  // Las conversaciones Support viven en el tenant del CLIENTE, así que la sala es `t:{clienteTenant}:c:`.
  try {
    if (tenantId === container.platform.getPlatformTenantId()) {
      const hasAgentPerm = (
        await checkPermission(principal, CommunicationPermissions.SupportAgent, container.userPermissions)
      ).allowed;
      if (hasAgentPerm || isPlatformAdminActorType(principal.actorType)) {
        const assigned = await container.supportTickets.listForAgentTenant({
          agentTenantId: tenantId,
          assignedAgentId: userId,
          take: MAX_JOINED_CONVERSATIONS,
          skip: 0,
          includeClosed: false,
        });
        const rooms = assigned.map((t) => `t:${t.tenantId}:c:${t.conversationId}`);
        if (rooms.length > 0) {
          await socket.join(rooms);
        }
      }
    }
  } catch (err) {
    logger.warn({ err, userId }, 'support join-on-connect: no se pudieron unir las salas de tickets asignados');
  }

  socket.on(ChatSocketEvents.StartDirectConversation, async (...args: unknown[]) => {
    const raw = args[0];
    const ack = typeof args[1] === 'function' ? (args[1] as (r: SocketAck<{ conversationId: string; wasCreated: boolean }>) => void) : undefined;
    const parsed = StartDirectConversationPayloadSchema.safeParse(raw);
    if (!parsed.success) {
      ack?.({ ok: false, code: 'Chat.BadPayload', message: parsed.error.message });
      return;
    }
    const startDirectPermCheck = await checkPermission(principal, CommunicationPermissions.ChatStart, container.userPermissions);
    if (!startDirectPermCheck.allowed) {
      ack?.({ ok: false, code: startDirectPermCheck.code, message: startDirectPermCheck.message });
      return;
    }
    const [recipientDisplayName, recipientActorType] = await Promise.all([
      resolveDisplayName(container.userDirectory, parsed.data.recipientUserId),
      resolveActorType(container.userDirectory, parsed.data.recipientUserId),
    ]);
    if (recipientActorType === null) {
      ack?.({ ok: false, code: 'Directory.Unresolved', message: 'Recipient actor type could not be resolved yet.' });
      return;
    }
    const result = await startDirectConversation(
      {
        tenantId,
        correlationId: socket.id,
        clientKey: parsed.data.clientKey,
        initiator: { userId, displayName: selfDisplayName, actorType: principal.actorType },
        recipient: { userId: parsed.data.recipientUserId, displayName: recipientDisplayName, actorType: recipientActorType },
      },
      container,
    );
    if (!result.isSuccess) {
      ack?.({ ok: false, code: result.error.code, message: result.error.message });
      return;
    }
    const directRoom = `t:${tenantId}:c:${result.value.conversationId}`;
    await socket.join(directRoom);
    // El receptor puede estar conectado y aún NO en la room (la conversación recién existe) — unir sus
    // sockets ya conectados para que reciba el primer mensaje/typing en vivo sin refrescar. Idempotente
    // y cross-instancia (via Redis adapter). El join-on-connect cubre el caso de que reconecte luego.
    io.in(`t:${tenantId}:u:${parsed.data.recipientUserId}`).socketsJoin(directRoom);
    ack?.({ ok: true, value: result.value });
  });

  /** F8b: actor de los comandos de grupo; el superpoder (D-22) = Tenant Admin o `group.manage_members`. */
  const groupActor = async (): Promise<GroupActor> => ({
    userId,
    displayName: selfDisplayName,
    actorType: principal.actorType,
    canAdministerAnyGroup:
      isTenantAdminActor(principal.actorType) ||
      (await checkPermission(principal, CommunicationPermissions.GroupManageMembers, container.userPermissions)).allowed,
  });
  const groupDeps = { ...container, emitter };

  /** T-12: la fila del listado del grupo recién creado, para que el creador la pinte sin releer la lista. */
  async function groupSummary(conversationId: string): Promise<ConversationListItem | null> {
    const conversation = await container.conversations.findById(tenantId, conversationId, 0);
    if (!conversation) return null;
    const snapshot = conversation.toSnapshot();
    const avatars = await resolveAvatars(container.avatars, snapshot.participants.map((p) => p.userId));
    return toConversationListItem(snapshot, userId, 0, avatars ? (id) => avatars.of(id) : undefined);
  }

  /** F8b: comandos de grupo con la misma forma (validar → caso de uso → ack por código). */
  function onGroupCommand<T, R>(
    event: string,
    schema: { safeParse(input: unknown): { success: true; data: T } | { success: false; error: { message: string } } },
    run: (data: T) => Promise<Result<R>>,
  ): void {
    socket.on(event, async (...args: unknown[]) => {
      const ack = typeof args[1] === 'function' ? (args[1] as (r: SocketAck<R>) => void) : undefined;
      const parsed = schema.safeParse(args[0]);
      if (!parsed.success) {
        ack?.({ ok: false, code: 'Chat.BadPayload', message: parsed.error.message });
        return;
      }
      try {
        const result = await run(parsed.data);
        ack?.(result.isSuccess ? { ok: true, value: result.value } : { ok: false, code: result.error.code, message: result.error.message });
      } catch (err) {
        logger.error({ err, event, userId }, 'group command failed');
        ack?.({ ok: false, code: 'Chat.Internal', message: 'Something went wrong. Try again.' });
      }
    });
  }

  socket.on(ChatSocketEvents.StartGroupConversation, async (...args: unknown[]) => {
    const raw = args[0];
    const ack =
      typeof args[1] === 'function'
        ? (args[1] as (r: SocketAck<{ conversationId: string; conversation: ConversationListItem | null }>) => void)
        : undefined;
    const parsed = StartGroupConversationPayloadSchema.safeParse(raw);
    if (!parsed.success) {
      ack?.({ ok: false, code: 'Chat.BadPayload', message: parsed.error.message });
      return;
    }
    const groupCreatePermCheck = await checkPermission(principal, CommunicationPermissions.GroupCreate, container.userPermissions);
    if (!groupCreatePermCheck.allowed) {
      ack?.({ ok: false, code: groupCreatePermCheck.code, message: groupCreatePermCheck.message });
      return;
    }
    const result = await startGroupConversation(
      {
        tenantId,
        correlationId: socket.id,
        clientKey: parsed.data.clientKey,
        creator: { userId, displayName: selfDisplayName, actorType: principal.actorType },
        title: parsed.data.title,
        description: parsed.data.description ?? null,
        memberUserIds: parsed.data.memberUserIds,
      },
      groupDeps,
    );
    if (!result.isSuccess) {
      ack?.({ ok: false, code: result.error.code, message: result.error.message });
      return;
    }
    const groupRoom = `t:${tenantId}:c:${result.value.conversationId}`;
    await socket.join(groupRoom);
    const summary = await groupSummary(result.value.conversationId);
    ack?.({ ok: true, value: { conversationId: result.value.conversationId, conversation: summary } });

    // A cada miembro y a las otras pestañas del creador (T-12): sus sockets entran a la sala y reciben
    // `created` por su sala personal para refrescar la lista. El join-on-connect cubre a los que reconecten.
    const createdDto: ConversationCreatedDto = {
      id: result.value.conversationId,
      kind: 'Group',
      title: parsed.data.title,
      createdByUserId: userId,
      createdAtUtc: new Date().toISOString(),
    };
    for (const memberUserId of [userId, ...result.value.memberUserIds]) {
      io.in(`t:${tenantId}:u:${memberUserId}`).socketsJoin(groupRoom);
      emitter.emitToUser({
        tenantId,
        userId: memberUserId,
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
    // F8b: quién agrega lo decide el rol en el grupo (aggregate); el permiso global solo amplía (superpoder).
    const result = await addGroupParticipant(
      {
        tenantId,
        correlationId: socket.id,
        clientKey: parsed.data.clientKey,
        conversationId: parsed.data.conversationId,
        actor: await groupActor(),
        newMemberUserId: parsed.data.newMemberUserId,
      },
      groupDeps,
    );
    if (!result.isSuccess) {
      ack?.({ ok: false, code: result.error.code, message: result.error.message });
      return;
    }
    ack?.({ ok: true, value: { conversationId: result.value.conversationId, newParticipantUserId: result.value.newParticipantUserId } });
    const [avatars, newMemberActorType] = await Promise.all([
      resolveAvatars(container.avatars, [result.value.newParticipantUserId]),
      resolveActorType(container.userDirectory, result.value.newParticipantUserId),
    ]);
    const addedDto: ConversationParticipantAddedDto = {
      conversationId: result.value.conversationId,
      addedByUserId: userId,
      newParticipantUserId: result.value.newParticipantUserId,
      newParticipantDisplayName: result.value.newParticipantDisplayName,
      ...(newMemberActorType ? { newParticipantActorType: newMemberActorType } : {}),
      ...(avatars ? { newParticipantAvatarFileId: avatars.of(result.value.newParticipantUserId) } : {}),
      addedAtUtc: new Date().toISOString(),
    };
    emitter.emitToConversation({
      tenantId,
      conversationId: result.value.conversationId,
      event: ChatSocketEvents.ConversationParticipantAdded,
      envelope: envelope(addedDto),
    });
    // Se le avisa por su room personal (para refrescar la lista) y se unen sus sockets ya conectados a
    // la room de la conversación, para que reciba los mensajes en vivo desde ya (join-on-connect cubre
    // los que reconecten). Se une DESPUÉS del emitToConversation de arriba para no duplicarle ese evento.
    emitter.emitToUser({
      tenantId,
      userId: result.value.newParticipantUserId,
      event: ChatSocketEvents.ConversationParticipantAdded,
      envelope: envelope(addedDto),
    });
    io.in(`t:${tenantId}:u:${result.value.newParticipantUserId}`).socketsJoin(
      `t:${tenantId}:c:${result.value.conversationId}`,
    );
  });

  socket.on(ChatSocketEvents.RemoveGroupParticipant, async (...args: unknown[]) => {
    const raw = args[0];
    const ack =
      typeof args[1] === 'function'
        ? (args[1] as (r: SocketAck<{ conversationId: string; removedParticipantUserId: string; reason: 'Left' | 'Kicked' }>) => void)
        : undefined;
    const parsed = RemoveGroupParticipantPayloadSchema.safeParse(raw);
    if (!parsed.success) {
      ack?.({ ok: false, code: 'Chat.BadPayload', message: parsed.error.message });
      return;
    }
    const targetUserId = parsed.data.targetUserId ?? userId;
    // F8b: salir nunca requiere permiso; expulsar lo decide el rol en el grupo (o el superpoder).
    const result = await removeGroupParticipant(
      {
        tenantId,
        correlationId: socket.id,
        clientKey: parsed.data.clientKey,
        conversationId: parsed.data.conversationId,
        actor: await groupActor(),
        targetUserId,
      },
      groupDeps,
    );
    if (!result.isSuccess) {
      ack?.({ ok: false, code: result.error.code, message: result.error.message });
      return;
    }
    ack?.({
      ok: true,
      value: {
        conversationId: result.value.conversationId,
        removedParticipantUserId: result.value.removedParticipantUserId,
        reason: result.value.reason,
      },
    });
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
    // Todos los sockets del que sale (otras pestañas o dispositivos) dejan la sala; si no, seguirían
    // recibiendo mensajes de un grupo del que ya no son parte.
    io.in(`t:${tenantId}:u:${targetUserId}`).socketsLeave(`t:${tenantId}:c:${result.value.conversationId}`);
  });

  onGroupCommand(ChatSocketEvents.SetGroupMemberRole, SetGroupMemberRolePayloadSchema, async (data) =>
    setGroupMemberRole(
      { tenantId, conversationId: data.conversationId, actor: await groupActor(), targetUserId: data.targetUserId, role: data.role },
      groupDeps,
    ),
  );

  onGroupCommand(ChatSocketEvents.TransferGroupOwnership, TransferGroupOwnershipPayloadSchema, async (data) =>
    transferGroupOwnership({ tenantId, conversationId: data.conversationId, actor: await groupActor(), targetUserId: data.targetUserId }, groupDeps),
  );

  onGroupCommand(ChatSocketEvents.UpdateGroupInfo, UpdateGroupInfoPayloadSchema, async (data) =>
    updateGroupInfo(
      {
        tenantId,
        conversationId: data.conversationId,
        actor: await groupActor(),
        ...(data.title !== undefined ? { title: data.title } : {}),
        ...(data.description !== undefined ? { description: data.description } : {}),
        ...(data.avatarFileId !== undefined ? { avatarFileId: data.avatarFileId } : {}),
      },
      groupDeps,
    ),
  );

  onGroupCommand(ChatSocketEvents.SetGroupAdminsOnly, SetGroupAdminsOnlyPayloadSchema, async (data) =>
    setGroupAdminsOnly({ tenantId, conversationId: data.conversationId, actor: await groupActor(), enabled: data.enabled }, groupDeps),
  );

  // ADD-05 H-6: silencio propio; las otras pestañas del usuario se enteran por su sala personal.
  onGroupCommand(ChatSocketEvents.SetConversationMuted, SetConversationMutedPayloadSchema, async (data) => {
    const result = await setConversationMuted({ tenantId, conversationId: data.conversationId, userId, muted: data.muted }, container);
    if (result.isSuccess) {
      emitter.emitToUser({
        tenantId,
        userId,
        event: ChatSocketEvents.ConversationUpdated,
        envelope: envelope({ conversationId: result.value.conversationId, kind: result.value.kind, isMuted: result.value.isMuted }),
      });
    }
    return result;
  });

  socket.on(ChatSocketEvents.SendMessage, async (...args: unknown[]) => {
    const raw = args[0];
    const ack = typeof args[1] === 'function' ? (args[1] as (r: SocketAck<{ message: MessageDto }>) => void) : undefined;
    const parsed = SendMessagePayloadSchema.safeParse(raw);
    if (!parsed.success) {
      ack?.({ ok: false, code: 'Chat.BadPayload', message: parsed.error.message });
      return;
    }
    const sendMessagePermCheck = await checkPermission(principal, CommunicationPermissions.ChatReply, container.userPermissions);
    if (!sendMessagePermCheck.allowed) {
      ack?.({ ok: false, code: sendMessagePermCheck.code, message: sendMessagePermCheck.message });
      return;
    }
    const allowed = await container.rateLimiter.allow({
      scope: CommunicationRateLimitPolicyNames.ChatSend,
      tenantId,
      userId,
      maxPerWindow: await container.tierAwareQuota.resolveMaxPerWindow(tenantId, config.rateLimit.chatSend.maxPerWindow),
      windowSeconds: config.rateLimit.chatSend.windowSeconds,
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
        audioDurationMs: parsed.data.audioDurationMs,
        audioWaveform: parsed.data.audioWaveform,
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

    // "Conectado = entregado (2 grises)" en vivo: marca delivered a los destinatarios con sesión viva
    // (aunque estén en otra página, sin el chat montado para auto-marcar) y avisa al emisor. El
    // backlog pre-conexión lo cubre el delivered-on-connect. Best-effort: nunca bloquea el envío.
    try {
      const deliveredReceipts = await markLiveDelivered(
        {
          tenantId,
          conversationId: parsed.data.conversationId,
          messageId: result.value.message.id,
          recipientUserIds: result.value.recipientUserIds,
        },
        container,
      );
      for (const receipt of deliveredReceipts) {
        emitter.emitToConversation({
          tenantId,
          conversationId: receipt.conversationId,
          event: ChatSocketEvents.MessageDelivered,
          envelope: envelope(receipt),
        });
      }
    } catch (err) {
      logger.warn({ err, userId }, 'mark-live-delivered: no se pudo marcar entregado en vivo');
    }
  });

  // ----- Support: chat del AGENTE sobre un ticket (cross-tenant, autoría del placeholder "Support Team") -----

  // El agente se une a la sala de la conversación del ticket para recibir mensajes/typing en vivo.
  socket.on(ChatSocketEvents.SupportAgentJoin, async (...args: unknown[]) => {
    const raw = args[0];
    const ack = typeof args[1] === 'function' ? (args[1] as (r: SocketAck<{ joined: boolean }>) => void) : undefined;
    const parsed = SupportAgentJoinPayloadSchema.safeParse(raw);
    if (!parsed.success) {
      ack?.({ ok: false, code: 'Support.BadPayload', message: parsed.error.message });
      return;
    }
    const ticket = await container.supportTickets.findById(parsed.data.ticketId);
    if (!ticket) {
      ack?.({ ok: false, code: 'Support.NotFound', message: 'Support ticket not found.' });
      return;
    }
    const hasAgentPerm = (
      await checkPermission(principal, CommunicationPermissions.SupportAgent, container.userPermissions)
    ).allowed;
    const canAccess = ticket.canBeAccessedBy({
      actorUserId: userId,
      actorTenantId: tenantId,
      actorHasAgentPermission: hasAgentPerm,
      isPlatformAdmin: isPlatformAdminActorType(principal.actorType),
    });
    if (!canAccess) {
      ack?.({ ok: false, code: 'Auth.Forbidden', message: 'Not allowed to read this support ticket.' });
      return;
    }
    const snap = ticket.toSnapshot();
    // La conversación vive en el tenant del CLIENTE: unir esa sala (no la del tenant del agente).
    await socket.join(`t:${snap.tenantId}:c:${snap.conversationId}`);
    ack?.({ ok: true, value: { joined: true } });
  });

  socket.on(ChatSocketEvents.SupportAgentSendMessage, async (...args: unknown[]) => {
    const raw = args[0];
    const ack = typeof args[1] === 'function' ? (args[1] as (r: SocketAck<{ message: MessageDto }>) => void) : undefined;
    const parsed = SupportAgentSendMessagePayloadSchema.safeParse(raw);
    if (!parsed.success) {
      ack?.({ ok: false, code: 'Support.BadPayload', message: parsed.error.message });
      return;
    }
    const isPlatformAdmin = isPlatformAdminActorType(principal.actorType);
    const hasAgentPerm = (
      await checkPermission(principal, CommunicationPermissions.SupportAgent, container.userPermissions)
    ).allowed;
    if (!hasAgentPerm && !isPlatformAdmin) {
      ack?.({ ok: false, code: 'Auth.Forbidden', message: 'Missing communication.support.agent.' });
      return;
    }
    // Rate-limit por el AGENTE real (no por el placeholder) — reusa la política de chat.
    const allowed = await container.rateLimiter.allow({
      scope: CommunicationRateLimitPolicyNames.ChatSend,
      tenantId,
      userId,
      maxPerWindow: await container.tierAwareQuota.resolveMaxPerWindow(tenantId, config.rateLimit.chatSend.maxPerWindow),
      windowSeconds: config.rateLimit.chatSend.windowSeconds,
    });
    if (!allowed) {
      ack?.({ ok: false, code: 'Chat.RateLimited', message: 'Too many messages, slow down.' });
      return;
    }
    const result = await sendSupportAgentMessage(
      {
        correlationId: socket.id,
        clientKey: parsed.data.clientKey,
        ticketId: parsed.data.ticketId,
        agent: { userId, tenantId, isPlatformAdmin },
        body: parsed.data.body,
        attachmentFileId: parsed.data.attachmentFileId,
        replyToMessageId: parsed.data.replyToMessageId,
        audioDurationMs: parsed.data.audioDurationMs,
        audioWaveform: parsed.data.audioWaveform,
      },
      container,
    );
    if (!result.isSuccess) {
      ack?.({ ok: false, code: result.error.code, message: result.error.message });
      return;
    }
    ack?.({ ok: true, value: { message: result.value.message } });
    // La conversación vive en el tenant del CLIENTE: emitir a ESA sala (el cliente y el agente unido la reciben).
    emitter.emitToConversation({
      tenantId: result.value.customerTenantId,
      conversationId: result.value.conversationId,
      event: ChatSocketEvents.MessageNew,
      envelope: envelope(result.value.message),
    });
    // Entregado en vivo al cliente si está online (cotejos sobre el mensaje de "Support Team").
    try {
      const deliveredReceipts = await markLiveDelivered(
        {
          tenantId: result.value.customerTenantId,
          conversationId: result.value.conversationId,
          messageId: result.value.message.id,
          recipientUserIds: result.value.recipientUserIds,
        },
        container,
      );
      for (const receipt of deliveredReceipts) {
        emitter.emitToConversation({
          tenantId: result.value.customerTenantId,
          conversationId: receipt.conversationId,
          event: ChatSocketEvents.MessageDelivered,
          envelope: envelope(receipt),
        });
      }
    } catch (err) {
      logger.warn({ err, userId }, 'support agent send: mark-live-delivered falló');
    }
  });

  socket.on(ChatSocketEvents.EditMessage, async (...args: unknown[]) => {
    const raw = args[0];
    const ack = typeof args[1] === 'function' ? (args[1] as (r: SocketAck<{ edited: unknown }>) => void) : undefined;
    const parsed = EditMessagePayloadSchema.safeParse(raw);
    if (!parsed.success) {
      ack?.({ ok: false, code: 'Chat.BadPayload', message: parsed.error.message });
      return;
    }
    const editMessagePermCheck = await checkPermission(principal, CommunicationPermissions.ChatReply, container.userPermissions);
    if (!editMessagePermCheck.allowed) {
      ack?.({ ok: false, code: editMessagePermCheck.code, message: editMessagePermCheck.message });
      return;
    }
    const allowed = await container.rateLimiter.allow({
      scope: CommunicationRateLimitPolicyNames.ChatEdit,
      tenantId,
      userId,
      maxPerWindow: await container.tierAwareQuota.resolveMaxPerWindow(tenantId, config.rateLimit.chatEdit.maxPerWindow),
      windowSeconds: config.rateLimit.chatEdit.windowSeconds,
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
    const canModerate = (
      await checkPermission(principal, CommunicationPermissions.ChatModerate, container.userPermissions)
    ).allowed;
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

  socket.on(ChatSocketEvents.MarkDelivered, async (...args: unknown[]) => {
    const raw = args[0];
    const ack = typeof args[1] === 'function' ? (args[1] as (r: SocketAck<{ markedCount: number }>) => void) : undefined;
    const parsed = MarkDeliveredPayloadSchema.safeParse(raw);
    if (!parsed.success) {
      ack?.({ ok: false, code: 'Chat.BadPayload', message: parsed.error.message });
      return;
    }
    const result = await markMessagesDelivered(
      {
        tenantId,
        conversationId: parsed.data.conversationId,
        userUserId: userId,
        upToMessageId: parsed.data.upToMessageId,
      },
      container,
    );
    if (!result.isSuccess) {
      ack?.({ ok: false, code: result.error.code, message: result.error.message });
      return;
    }
    ack?.({ ok: true, value: { markedCount: result.value.markedCount } });
    // Solo vale la pena avisar al emisor si de verdad se marcó algo nuevo (evita ruido en cada
    // mensaje ya entregado). El emisor pinta los 2 cotejos grises sobre sus mensajes ≤ upToMessageId.
    if (result.value.markedCount > 0) {
      emitter.emitToConversation({
        tenantId,
        conversationId: parsed.data.conversationId,
        event: ChatSocketEvents.MessageDelivered,
        envelope: envelope(result.value.receipt),
      });
    }
  });

  const joinedConversationTenant = (conversationId: string): string | null =>
    resolveJoinedConversationTenant(socket.rooms, tenantId, conversationId);

  const indicatorIdentity = (conversationTenantId: string): { userId: string; displayName: string } =>
    conversationTenantId === tenantId
      ? { userId, displayName: selfDisplayName }
      : { userId: tenantId, displayName: 'Support Team' };

  const emitTypingStopped = (conversationTenantId: string, conversationId: string): void => {
    const identity = indicatorIdentity(conversationTenantId);
    emitter.emitToConversation({
      tenantId: conversationTenantId,
      conversationId,
      event: ChatSocketEvents.TypingStopped,
      envelope: envelope({ conversationId, userId: identity.userId, displayName: identity.displayName }),
    });
  };

  socket.on(ChatSocketEvents.TypingStart, async (...args: unknown[]) => {
    const parsed = TypingPayloadSchema.safeParse(args[0]);
    if (!parsed.success) return;
    const conversationTenantId = joinedConversationTenant(parsed.data.conversationId);
    if (!conversationTenantId) return;
    const allowed = await container.rateLimiter.allow({
      scope: CommunicationRateLimitPolicyNames.ChatTyping,
      tenantId,
      userId,
      maxPerWindow: await container.tierAwareQuota.resolveMaxPerWindow(tenantId, config.rateLimit.chatTyping.maxPerWindow),
      windowSeconds: config.rateLimit.chatTyping.windowSeconds,
    });
    if (!allowed) return;
    const { conversationId } = parsed.data;
    const identity = indicatorIdentity(conversationTenantId);
    emitter.emitToConversation({
      tenantId: conversationTenantId,
      conversationId,
      event: ChatSocketEvents.TypingStarted,
      envelope: envelope({ conversationId, userId: identity.userId, displayName: identity.displayName }),
    });

    const key = typingKey(conversationTenantId, conversationId, userId);
    clearTypingTimer(key);
    const timer = setTimeout(() => {
      typingTimers.delete(key);
      emitTypingStopped(conversationTenantId, conversationId);
    }, TYPING_TIMEOUT_MS);
    typingTimers.set(key, { timer, conversationTenantId, conversationId });
  });

  socket.on(ChatSocketEvents.TypingStop, (...args: unknown[]) => {
    const parsed = TypingPayloadSchema.safeParse(args[0]);
    if (!parsed.success) return;
    const conversationTenantId = joinedConversationTenant(parsed.data.conversationId);
    if (!conversationTenantId) return;
    clearTypingTimer(typingKey(conversationTenantId, parsed.data.conversationId, userId));
    emitTypingStopped(conversationTenantId, parsed.data.conversationId);
  });

  // ---------- Indicador "grabando una nota de voz…" (clon del typing) ----------

  const emitRecordingStopped = (conversationTenantId: string, conversationId: string): void => {
    const identity = indicatorIdentity(conversationTenantId);
    emitter.emitToConversation({
      tenantId: conversationTenantId,
      conversationId,
      event: ChatSocketEvents.RecordingStopped,
      envelope: envelope({ conversationId, userId: identity.userId, displayName: identity.displayName }),
    });
  };

  socket.on(ChatSocketEvents.RecordingStart, async (...args: unknown[]) => {
    const parsed = RecordingPayloadSchema.safeParse(args[0]);
    if (!parsed.success) return;
    const conversationTenantId = joinedConversationTenant(parsed.data.conversationId);
    if (!conversationTenantId) return;
    const allowed = await container.rateLimiter.allow({
      scope: CommunicationRateLimitPolicyNames.ChatVoiceRecording,
      tenantId,
      userId,
      maxPerWindow: await container.tierAwareQuota.resolveMaxPerWindow(
        tenantId,
        config.rateLimit.chatVoiceRecording.maxPerWindow,
      ),
      windowSeconds: config.rateLimit.chatVoiceRecording.windowSeconds,
    });
    if (!allowed) return;
    const { conversationId } = parsed.data;
    const identity = indicatorIdentity(conversationTenantId);
    emitter.emitToConversation({
      tenantId: conversationTenantId,
      conversationId,
      event: ChatSocketEvents.RecordingStarted,
      envelope: envelope({ conversationId, userId: identity.userId, displayName: identity.displayName }),
    });

    const key = typingKey(conversationTenantId, conversationId, userId);
    clearRecordingTimer(key);
    const timer = setTimeout(() => {
      recordingTimers.delete(key);
      emitRecordingStopped(conversationTenantId, conversationId);
    }, RECORDING_TIMEOUT_MS);
    recordingTimers.set(key, { timer, conversationTenantId, conversationId });
  });

  socket.on(ChatSocketEvents.RecordingStop, (...args: unknown[]) => {
    const parsed = RecordingPayloadSchema.safeParse(args[0]);
    if (!parsed.success) return;
    const conversationTenantId = joinedConversationTenant(parsed.data.conversationId);
    if (!conversationTenantId) return;
    clearRecordingTimer(typingKey(conversationTenantId, parsed.data.conversationId, userId));
    emitRecordingStopped(conversationTenantId, parsed.data.conversationId);
  });

  // Snapshot de presencia bajo demanda: `chat.presence.changed` solo se emite en transiciones, así que
  // un peer ya conectado antes de abrir el chat nunca se reflejaba. El cliente pide el estado de sus
  // peers al seleccionar la conversación; se responde SOLO a este socket, reusando el mismo evento.
  socket.on(ChatSocketEvents.PresenceQuery, async (...args: unknown[]) => {
    const parsed = PresenceQueryPayloadSchema.safeParse(args[0]);
    if (!parsed.success) return;

    // Para el portal, acotamos los userIds consultados al staff asignado al cliente.
    // El staff no tiene restricción extra: ya opera dentro del tenant.
    let allowedUserIds: readonly string[] = parsed.data.userIds;
    if (principal.actorType === 'CustomerPortal') {
      const visible = new Set(
        await container.presenceAudiences.findStaffUserIdsVisibleToPortalUser(tenantId, userId),
      );
      allowedUserIds = parsed.data.userIds.filter((id) => visible.has(id));
      if (allowedUserIds.length === 0) return;
    }

    // F8c: estado completo (incluido Busy en llamada o reunión) para pintar el punto correcto al cargar.
    const now = new Date().toISOString();
    const states = container.presence.snapshot
      ? await container.presence.snapshot(tenantId, allowedUserIds)
      : await container.presence.listOnline(tenantId, allowedUserIds).then((online) => {
          const set = new Set(online);
          return allowedUserIds.map((id) => ({ userId: id, status: set.has(id) ? ('Online' as const) : ('Offline' as const), busyReason: null }));
        });
    for (const state of states) {
      socket.emit(ChatSocketEvents.PresenceChanged, envelope({ ...state, changedAtUtc: now }));
    }
  });

  /** F8d: agregar/quitar reacciones comparten el límite por persona (configuración). */
  const allowReaction = async (): Promise<boolean> =>
    container.rateLimiter.allow({
      scope: CommunicationRateLimitPolicyNames.ChatReaction,
      tenantId,
      userId,
      maxPerWindow: config.rateLimit.chatReaction.maxPerWindow,
      windowSeconds: config.rateLimit.chatReaction.windowSeconds,
    });

  socket.on(ChatSocketEvents.AddReaction, async (...args: unknown[]) => {
    const raw = args[0];
    const ack = typeof args[1] === 'function' ? (args[1] as (r: SocketAck<{ wasNew: boolean }>) => void) : undefined;
    const parsed = AddReactionPayloadSchema.safeParse(raw);
    if (!parsed.success) {
      ack?.({ ok: false, code: 'Chat.BadPayload', message: parsed.error.message });
      return;
    }
    const addReactionPermCheck = await checkPermission(principal, CommunicationPermissions.ChatReply, container.userPermissions);
    if (!addReactionPermCheck.allowed) {
      ack?.({ ok: false, code: addReactionPermCheck.code, message: addReactionPermCheck.message });
      return;
    }
    if (!(await allowReaction())) {
      ack?.({ ok: false, code: 'Chat.RateLimited', message: 'Too many reactions, slow down.' });
      return;
    }
    const result = await addMessageReaction(
      {
        tenantId,
        correlationId: socket.id,
        messageId: parsed.data.messageId,
        userId,
        emoji: parsed.data.emoji,
      },
      { ...container, emitter },
    );
    if (!result.isSuccess) {
      ack?.({ ok: false, code: result.error.code, message: result.error.message });
      return;
    }
    ack?.({ ok: true, value: result.value });
  });

  socket.on(ChatSocketEvents.RemoveReaction, async (...args: unknown[]) => {
    const raw = args[0];
    const ack = typeof args[1] === 'function' ? (args[1] as (r: SocketAck<{ wasPresent: boolean }>) => void) : undefined;
    const parsed = RemoveReactionPayloadSchema.safeParse(raw);
    if (!parsed.success) {
      ack?.({ ok: false, code: 'Chat.BadPayload', message: parsed.error.message });
      return;
    }
    if (!(await allowReaction())) {
      ack?.({ ok: false, code: 'Chat.RateLimited', message: 'Too many reactions, slow down.' });
      return;
    }
    const result = await removeMessageReaction(
      {
        tenantId,
        correlationId: socket.id,
        messageId: parsed.data.messageId,
        userId,
        emoji: parsed.data.emoji,
      },
      { ...container, emitter },
    );
    if (!result.isSuccess) {
      ack?.({ ok: false, code: result.error.code, message: result.error.message });
      return;
    }
    ack?.({ ok: true, value: result.value });
  });

  socket.on(ChatSocketEvents.PinMessage, async (...args: unknown[]) => {
    const raw = args[0];
    const ack = typeof args[1] === 'function' ? (args[1] as (r: SocketAck<{ wasNew: boolean }>) => void) : undefined;
    const parsed = PinMessagePayloadSchema.safeParse(raw);
    if (!parsed.success) {
      ack?.({ ok: false, code: 'Chat.BadPayload', message: parsed.error.message });
      return;
    }
    const result = await pinMessage(
      {
        tenantId,
        correlationId: socket.id,
        messageId: parsed.data.messageId,
        actorUserId: userId,
        actorType: principal.actorType,
        actorPermissionVersion: principal.permissionVersion,
      },
      { ...container, emitter },
    );
    if (!result.isSuccess) {
      ack?.({ ok: false, code: result.error.code, message: result.error.message });
      return;
    }
    ack?.({ ok: true, value: result.value });
  });

  socket.on(ChatSocketEvents.UnpinMessage, async (...args: unknown[]) => {
    const raw = args[0];
    const ack = typeof args[1] === 'function' ? (args[1] as (r: SocketAck<{ wasPinned: boolean }>) => void) : undefined;
    const parsed = UnpinMessagePayloadSchema.safeParse(raw);
    if (!parsed.success) {
      ack?.({ ok: false, code: 'Chat.BadPayload', message: parsed.error.message });
      return;
    }
    const result = await unpinMessage(
      {
        tenantId,
        correlationId: socket.id,
        messageId: parsed.data.messageId,
        actorUserId: userId,
        actorType: principal.actorType,
        actorPermissionVersion: principal.permissionVersion,
      },
      { ...container, emitter },
    );
    if (!result.isSuccess) {
      ack?.({ ok: false, code: result.error.code, message: result.error.message });
      return;
    }
    ack?.({ ok: true, value: result.value });
  });

  socket.on(ChatSocketEvents.ForwardMessage, async (...args: unknown[]) => {
    const raw = args[0];
    const ack = typeof args[1] === 'function' ? (args[1] as (r: SocketAck<{ message: MessageDto }>) => void) : undefined;
    const parsed = ForwardMessagePayloadSchema.safeParse(raw);
    if (!parsed.success) {
      ack?.({ ok: false, code: 'Chat.BadPayload', message: parsed.error.message });
      return;
    }
    const forwardMessagePermCheck = await checkPermission(principal, CommunicationPermissions.ChatReply, container.userPermissions);
    if (!forwardMessagePermCheck.allowed) {
      ack?.({ ok: false, code: forwardMessagePermCheck.code, message: forwardMessagePermCheck.message });
      return;
    }
    const result = await forwardMessage(
      {
        tenantId,
        correlationId: socket.id,
        clientKey: parsed.data.clientKey,
        originMessageId: parsed.data.originMessageId,
        targetConversationId: parsed.data.targetConversationId,
        forwarderUserId: userId,
      },
      { ...container, emitter },
    );
    if (!result.isSuccess) {
      ack?.({ ok: false, code: result.error.code, message: result.error.message });
      return;
    }
    ack?.({ ok: true, value: result.value });
  });

  socket.on('disconnect', () => {
    clearInterval(heartbeat);
    // Baja diferida (ver PRESENCE_DISCONNECT_GRACE_MS): tolera la reconexión de la
    // microcaída sin parpadear a Offline. La sesión vieja mantiene su lease TTL
    // mientras tanto; unregister solo publica Offline si al ejecutarse ya no queda
    // ninguna sesión del usuario.
    setTimeout(() => {
      container.presence
        .unregister({ tenantId, userId, sessionId: socket.id })
        .catch((err) => logger.warn({ err }, 'presence unregister failed'));
    }, PRESENCE_DISCONNECT_GRACE_MS).unref();

    // Cierra cualquier indicador de "escribiendo..." que este socket dejo
    // pendiente — sin esto, un cierre abrupto de pestana deja el indicador
    // pegado hasta el timeout de 8s (que igual dispara, pero mejor limpiar ya).
    for (const [key, entry] of typingTimers) {
      if (!key.endsWith(`:${userId}`)) continue;
      clearTypingTimer(key);
      emitTypingStopped(entry.conversationTenantId, entry.conversationId);
    }

    // Igual para "grabando una nota de voz…": un cierre abrupto no debe dejar el indicador pegado.
    for (const [key, entry] of recordingTimers) {
      if (!key.endsWith(`:${userId}`)) continue;
      clearRecordingTimer(key);
      emitRecordingStopped(entry.conversationTenantId, entry.conversationId);
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
