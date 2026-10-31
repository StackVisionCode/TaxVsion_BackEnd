import { randomUUID } from 'node:crypto';
import { config } from '../../../infrastructure/config.js';
import { logger } from '../../../infrastructure/logger/logger.js';
import { checkPermission, CommunicationPermissions } from '../../../domain/shared/permissions.js';
import type { AppContainer } from '../../../infrastructure/container.js';
import type { CommunicationIoServer, CommunicationSocket } from '../../../infrastructure/socket/build-io.js';
import { SocketRealtimeEmitter } from '../../../infrastructure/socket/socket-realtime-emitter.js';
import { resolveDisplayName } from './resolve-display-name.js';
import { CommunicationRateLimitPolicyNames } from '../../../domain/rate-limit/rate-limit-policies.js';
import { TECHNICAL_LIMITS } from '../../../domain/settings/technical-limits.js';
import {
  AdmitPayloadSchema,
  AttachMeetingRecordingPayloadSchema,
  CancelMeetingPayloadSchema,
  DemoteCohostPayloadSchema,
  DenyParticipantPayloadSchema,
  LowerAllHandsPayloadSchema,
  LowerHandPayloadSchema,
  JoinMeetingPayloadSchema,
  RejoinMeetingPayloadSchema,
  LeaveMeetingPayloadSchema,
  LockPayloadSchema,
  PromoteCohostPayloadSchema,
  RescheduleMeetingPayloadSchema,
  MeetingChatDeletePayloadSchema,
  MeetingChatEditPayloadSchema,
  MeetingChatMarkReadPayloadSchema,
  MeetingChatSendPayloadSchema,
  MeetingMediaStatusPayloadSchema,
  MeetingRaiseHandPayloadSchema,
  MeetingReactionPayloadSchema,
  MeetingSpeakingPayloadSchema,
  MeetingConnectionQualityPayloadSchema,
  MeetingSignalPayloadSchema,
  MeetingSocketEvents,
  RemovePayloadSchema,
  RequestMeetingRecordingPayloadSchema,
  RespondMeetingRecordingConsentPayloadSchema,
  SfuConnectTransportPayloadSchema,
  SfuConsumePayloadSchema,
  SfuCreateTransportPayloadSchema,
  SfuListRemoteProducersPayloadSchema,
  SfuProducePayloadSchema,
  SfuRestartIcePayloadSchema,
  SfuResumeConsumerPayloadSchema,
  SfuSetPreferredLayersPayloadSchema,
  SfuRouterCapabilitiesPayloadSchema,
  StopMeetingRecordingPayloadSchema,
  TransferHostPayloadSchema,
  type MeetingParticipantDto,
  type MeetingHandLoweredByHostDto,
  type MeetingRaiseHandAckDto,
  type MessageDeletedDto,
  type MessageDto,
  type MessageEditedDto,
  type SfuNewProducerDto,
  type SfuProducerClosedDto,
} from '../../../contracts/socket/meeting-socket-events.js';
import type { SocketAck, SocketEnvelope } from '../../../contracts/socket/socket-envelope.js';
import { joinMeeting, type JoinMeetingResult } from '../../../application/use-cases/join-meeting.js';
import { rejoinMeeting, type RejoinMeetingResult } from '../../../application/use-cases/rejoin-meeting.js';
import { leaveMeeting } from '../../../application/use-cases/leave-meeting.js';
import { relayMeetingSignal } from '../../../application/use-cases/relay-meeting-signal.js';
import { updateRaiseHand } from '../../../application/use-cases/update-meeting-media-status.js';
import { lowerAllHands, lowerParticipantHand } from '../../../application/use-cases/meeting-hand-actions.js';
import { sendMeetingReaction } from '../../../application/services/meeting-reaction.service.js';
import { recordMeetingEvent } from '../../../application/use-cases/meeting-event-log-writer.js';
import { MeetingEventType } from '../../../domain/meetings/meeting-event-log.js';
import {
  admitParticipant,
  muteAllInMeeting,
  removeParticipant,
  setMeetingLocked,
  transferMeetingHost,
} from '../../../application/use-cases/meeting-host-actions.js';
import { attachRecording, requestRecording, respondRecordingConsent, stopRecording } from '../../../application/services/recording-application.service.js';
import { updateMediaStatus } from '../../../application/services/media-status.service.js';
import { reportConnectionQuality } from '../../../application/services/connection-quality.service.js';
import { cancelMeeting } from '../../../application/use-cases/cancel-meeting.js';
import { rescheduleMeeting } from '../../../application/use-cases/reschedule-meeting.js';
import { denyWaitingRoomParticipant } from '../../../application/use-cases/deny-waiting-room-participant.js';
import {
  demoteCohostToAttendee,
  promoteParticipantToCohost,
} from '../../../application/use-cases/change-participant-role.js';
import type { RecordingConsentEntryStatus } from '../../../domain/recording/recording-consent.js';
import {
  connectSfuTransport,
  consumeSfuMedia,
  createSfuTransport,
  getSfuRouterCapabilities,
  listSfuRemoteProducers,
  produceSfuMedia,
  restartSfuTransportIce,
  resumeSfuConsumer,
  setSfuConsumerPreferredLayers,
} from '../../../application/use-cases/sfu-signaling.js';
import { MeetingSocketRegistry } from './meeting-socket-registry.js';
import { sendMessage } from '../../../application/use-cases/send-message.js';
import { editMessage } from '../../../application/use-cases/edit-message.js';
import { authorizeMeetingChatSend } from '../../../application/use-cases/meeting-chat-send-policy.js';
import { ChatSocketEvents } from '../../../contracts/socket/chat-socket-events.js';
import { deleteMessage } from '../../../application/use-cases/delete-message.js';
import { markMessagesRead } from '../../../application/use-cases/mark-messages-read.js';
import { computeMeetingUniquenessKey } from '../../../domain/conversations/uniqueness-key.js';
import { forceLeaveMeeting } from '../../../application/use-cases/force-leave-meeting.js';
import type { MeetingHostChangedDto, MeetingYouRemovedDto } from '../../../contracts/socket/meeting-socket-events.js';

export function registerMeetingHandlers(io: CommunicationIoServer, container: AppContainer): void {
  const emitter = new SocketRealtimeEmitter(io);
  const registry = new MeetingSocketRegistry();
  io.on('connection', (socket) => {
    wireMeetingSocket(socket, io, container, emitter, registry);
  });
}

/**
 * Fase A3 — mismo criterio de respaldo que CALL_BUSY_LEASE_SECONDS: en
 * operacion normal siempre se limpia explicitamente (leave/remove/disconnect),
 * este TTL solo protege contra un crash a mitad de un `markBusy`.
 */
const MEETING_BUSY_LEASE_SECONDS = 6 * 60 * 60;

/**
 * Limpia la fuente de "busy" de este meeting para una lista de usuarios.
 * Se usa tanto para un solo participante que se va (Leave/Remove/disconnect)
 * como para TODOS los participantes cuando el meeting termina en cascada
 * (el host se va sin cohost disponible -> Meeting.end() marca a todos Left
 * en la misma llamada de dominio, sin que sus propios sockets disparen un
 * Leave individual) — sin este segundo caso, esos participantes quedarian
 * "Busy" indefinidamente hasta que el TTL de respaldo expire.
 */
async function clearMeetingBusyFor(
  container: AppContainer,
  tenantId: string,
  meetingId: string,
  userIds: readonly string[],
): Promise<void> {
  await Promise.all(
    userIds.map((participantUserId) =>
      container.presence
        .clearBusy({ tenantId, userId: participantUserId, sourceId: meetingId })
        .catch((err: unknown) => logger.warn({ err, meetingId }, 'presence clearBusy (meeting) failed')),
    ),
  );
}

/**
 * Gracia de reconexión ante un `disconnect`: en vez de sacar al participante de una vez (lo que hacía
 * que un blip de red o un cambio de pestaña del host terminara el meeting — host se va sin cohost →
 * Meeting.end()), se AGENDA su salida tras la gracia vigente (`meeting.disconnectGraceSeconds` del
 * resolver, global). Si el MISMO usuario reconecta (join/rejoin) dentro de la ventana, se cancela.
 * Registro en memoria por (tenant:meeting:user); es válido en la flota local single-instance (el churn
 * de cloudflared reconecta en segundos).
 */
const pendingLeaves = new Map<string, ReturnType<typeof setTimeout>>();
const leaveKey = (tenantId: string, meetingId: string, userId: string): string =>
  `${tenantId}:${meetingId}:${userId}`;

function cancelPendingLeave(tenantId: string, meetingId: string, userId: string): void {
  const key = leaveKey(tenantId, meetingId, userId);
  const timer = pendingLeaves.get(key);
  if (timer) {
    clearTimeout(timer);
    pendingLeaves.delete(key);
  }
}

/**
 * La gracia venció sin reconexión: salida forzada única (T-08) — saca al participante, avisa a la sala,
 * libera SFU y "busy", y si era el host aplica la sucesión (D-08) o el cierre ordenado.
 */
async function performLeaveAfterGrace(
  container: AppContainer,
  emitter: SocketRealtimeEmitter,
  tenantId: string,
  meetingId: string,
  userId: string,
): Promise<void> {
  const result = await forceLeaveMeeting(
    { tenantId, meetingId, userId, reason: 'Grace', correlationId: `disconnect:${userId}` },
    { ...container, emitter },
  ).catch((err: unknown) => {
    logger.warn({ err, meetingId }, 'leave meeting after grace failed');
    return null;
  });
  if (!result || !result.isSuccess) return;
}

/**
 * Avisa a las LISTAS de meetings (room de usuario `t:{tenant}:u:{userId}`) que un meeting terminó, para
 * que participantes e invitados vean la fila pasar de "upcoming" a "past" sin recargar. Se usa tanto en
 * el fin explícito (endMeeting) como en la cascada por disconnect.
 */
async function emitMeetingEndedToLists(
  container: AppContainer,
  emitter: SocketRealtimeEmitter,
  tenantId: string,
  meetingId: string,
  participantUserIds: readonly string[],
): Promise<void> {
  const invitations = await container.meetings.listInvitationsByMeeting(tenantId, meetingId);
  const inviteeUserIds = invitations
    .map((inv) => inv.toSnapshot())
    .filter((s) => s.revokedAtUtc === null && s.inviteeUserId !== null)
    .map((s) => s.inviteeUserId!);
  for (const uid of new Set<string>([...participantUserIds, ...inviteeUserIds])) {
    emitter.emitToUser({
      tenantId,
      userId: uid,
      event: MeetingSocketEvents.Ended,
      envelope: envelope({ meetingId }),
    });
  }
}

function wireMeetingSocket(
  socket: CommunicationSocket,
  io: CommunicationIoServer,
  container: AppContainer,
  emitter: SocketRealtimeEmitter,
  registry: MeetingSocketRegistry,
): void {
  const principal = socket.data.principal;
  if (!principal) return;
  const { tenantId, userId } = principal;

  // Avisa a los demas participantes que los producers de `userId` van a
  // desaparecer, y libera sus transports/producers/consumers en el SFU. Se
  // llama tanto en Leave explicito como en disconnect abrupto.
  const closeSfuForParticipant = async (meetingId: string, leavingUserId: string): Promise<void> => {
    const ownProducers = container.sfu.listProducersForUser(meetingId, leavingUserId);
    for (const p of ownProducers) {
      const closedDto: SfuProducerClosedDto = { meetingId, userId: leavingUserId, producerId: p.producerId };
      emitter.emitToMeeting({
        tenantId,
        meetingId,
        event: MeetingSocketEvents.SfuProducerClosed,
        envelope: envelope(closedDto),
      });
    }
    await container.sfu
      .closeParticipant(meetingId, leavingUserId)
      .catch((err: unknown) =>
        logger.warn({ err, meetingId, userId: leavingUserId }, 'sfu closeParticipant failed'),
      );
  };

  // INV-07 H-10: las acciones de host exigen el permiso `communication.meeting.host` además del rol
  // en el agregado (permiso ≠ recurso). Un guest no tiene permisos, así que nunca pasa de aquí.
  const requireHostPermission = async (ack?: (r: SocketAck<never>) => void): Promise<boolean> => {
    const check = await checkPermission(principal, CommunicationPermissions.MeetingHost, container.userPermissions);
    if (check.allowed) return true;
    ack?.({ ok: false, code: check.code, message: check.message });
    return false;
  };

  socket.on(MeetingSocketEvents.Join, async (...args: unknown[]) => {
    const raw = args[0];
    const ack =
      typeof args[1] === 'function' ? (args[1] as (r: SocketAck<JoinMeetingResult>) => void) : undefined;
    const parsed = JoinMeetingPayloadSchema.safeParse(raw);
    if (!parsed.success) {
      ack?.({ ok: false, code: 'Meeting.BadPayload', message: parsed.error.message });
      return;
    }

    // Fase Backend 5 — un guest (ticket firmado, ver build-io.ts/join-ticket.ts)
    // no tiene permissions reales (siempre []), asi que el chequeo normal de
    // hasPermission fallaria siempre. El ticket YA es la autorizacion — esta
    // scoped a un unico meetingId (claim `meeting_id`), que se valida aca para
    // que un guest no pueda usar su ticket para entrar a OTRO meeting. Todas
    // las demas acciones (record/cohost/promote) siguen bloqueadas para Guest
    // porque sus permissions vacios hacen fallar hasPermission en esos handlers.
    const isGuest = principal.actorType === 'Guest';
    if (isGuest) {
      const ticketMeetingId =
        typeof principal.raw['meeting_id'] === 'string' ? principal.raw['meeting_id'] : undefined;
      if (!ticketMeetingId || ticketMeetingId !== parsed.data.meetingId) {
        ack?.({ ok: false, code: 'Auth.Forbidden', message: 'Guest ticket is not scoped to this meeting.' });
        return;
      }
    } else {
      const joinPermCheck = await checkPermission(
        principal,
        CommunicationPermissions.MeetingJoin,
        container.userPermissions,
      );
      if (!joinPermCheck.allowed) {
        ack?.({ ok: false, code: joinPermCheck.code, message: joinPermCheck.message });
        return;
      }
    }

    const guestDisplayName =
      typeof principal.raw['display_name'] === 'string' ? principal.raw['display_name'] : undefined;
    const guestInvitationId =
      typeof principal.raw['invitation_id'] === 'string' ? principal.raw['invitation_id'] : undefined;
    // Fallback legible cuando el directorio no tiene fila (owner viejo de onboarding / race): el email
    // del JWT, para que la tile del participante nunca muestre el GUID crudo.
    const selfEmail = typeof principal.raw['email'] === 'string' ? principal.raw['email'] : undefined;
    const selfDisplayName = isGuest
      ? (guestDisplayName ?? 'Invitado')
      : await resolveDisplayName(container.userDirectory, userId, selfEmail);
    const result = await joinMeeting(
      {
        tenantId,
        correlationId: socket.id,
        meetingId: parsed.data.meetingId,
        user: { userId, displayName: selfDisplayName, actorType: principal.actorType },
        ...(parsed.data.passcode !== undefined ? { passcode: parsed.data.passcode } : {}),
        ...(parsed.data.invitationToken !== undefined
          ? { invitationToken: parsed.data.invitationToken }
          : {}),
        ...(isGuest && guestInvitationId !== undefined ? { guestInvitationId } : {}),
        ...(parsed.data.audioDefault !== undefined ? { audioDefault: parsed.data.audioDefault } : {}),
        ...(parsed.data.videoDefault !== undefined ? { videoDefault: parsed.data.videoDefault } : {}),
      },
      container,
    );
    if (!result.isSuccess) {
      ack?.({ ok: false, code: result.error.code, message: result.error.message });
      return;
    }
    await socket.join(`t:${tenantId}:m:${parsed.data.meetingId}`);
    if (result.value.snapshot.conversationId) {
      await socket.join(`t:${tenantId}:c:${result.value.snapshot.conversationId}`);
    }
    // Reconectó / volvió a entrar: cancelar cualquier salida agendada por un disconnect anterior.
    cancelPendingLeave(tenantId, parsed.data.meetingId, userId);
    registry.register(tenantId, parsed.data.meetingId, userId, socket.id);
    if (result.value.rejoined) {
      // Media nueva (recarga/otra pestaña): los producers viejos del SFU no vuelven. Avisar y liberar
      // antes de que el cliente cree sus transports, para que nadie conserve consumers huérfanos.
      await closeSfuForParticipant(parsed.data.meetingId, userId);
    }
    ack?.({ ok: true, value: result.value });
    // Avisar a la room del cambio de participante — SIEMPRE, entre directo (status Joined) o en sala de
    // espera (status Waiting). El caso Waiting es clave: sin este emit el host NUNCA se enteraba de que
    // alguien esperaba admisión (antes solo se emitía en el join directo), así que su UX de
    // admitir/denegar quedaba vacía y el que esperaba nunca era admitido.
    const me = result.value.snapshot.participants.find((p) => p.userId === userId);
    if (me) {
      emitter.emitToMeeting({
        tenantId,
        meetingId: parsed.data.meetingId,
        event: MeetingSocketEvents.ParticipantChanged,
        envelope: envelope({ meetingId: parsed.data.meetingId, participant: me, sequence: 0 }),
      });
    }
    if (!result.value.requiresAdmission) {
      // Entra directo (sin sala de espera) -> ya esta Joined de verdad: reservar presencia "busy".
      await container.presence
        .markBusy({
          tenantId,
          userId,
          sourceId: parsed.data.meetingId,
          kind: 'Meeting',
          leaseSeconds: MEETING_BUSY_LEASE_SECONDS,
        })
        .catch((err: unknown) => logger.warn({ err }, 'presence markBusy (meeting join) failed'));
    }
  });

  // Re-suscripción tras reconnect transparente del socket (churn), sin recargar la página: re-une la
  // room `m:{meetingId}` para un participante YA admitido, SIN media ni admisión. La room de chat del
  // meeting la re-une el join-on-connect. Devuelve el snapshot para reconciliar participantes.
  socket.on(MeetingSocketEvents.Rejoin, async (...args: unknown[]) => {
    const ack =
      typeof args[1] === 'function' ? (args[1] as (r: SocketAck<RejoinMeetingResult>) => void) : undefined;
    const parsed = RejoinMeetingPayloadSchema.safeParse(args[0]);
    if (!parsed.success) {
      ack?.({ ok: false, code: 'Meeting.BadPayload', message: parsed.error.message });
      return;
    }
    // La autorización es ser participante Joined (lo valida rejoinMeeting) — no re-chequea el permiso
    // de join: ya lo pasó al entrar, y un ticket de guest sigue acotado a su meeting.
    const result = await rejoinMeeting({ tenantId, meetingId: parsed.data.meetingId, userId }, container);
    if (!result.isSuccess) {
      ack?.({ ok: false, code: result.error.code, message: result.error.message });
      return;
    }
    await socket.join(`t:${tenantId}:m:${parsed.data.meetingId}`);
    // Reconexión transparente (churn): cancelar la salida agendada por el disconnect que la disparó.
    cancelPendingLeave(tenantId, parsed.data.meetingId, userId);
    registry.register(tenantId, parsed.data.meetingId, userId, socket.id);
    ack?.({ ok: true, value: result.value });
  });

  socket.on(MeetingSocketEvents.Leave, async (...args: unknown[]) => {
    const parsed = LeaveMeetingPayloadSchema.safeParse(args[0]);
    if (!parsed.success) return;
    const result = await leaveMeeting(
      { tenantId, correlationId: socket.id, meetingId: parsed.data.meetingId, userId },
      container,
    );
    if (!result.isSuccess) return;
    // Salida explícita: no dejar una salida agendada colgando para este user/meeting.
    cancelPendingLeave(tenantId, parsed.data.meetingId, userId);
    registry.unregister(tenantId, parsed.data.meetingId, userId, socket.id);
    // Avisar a la room que este participante salió (status:'Left') para que los demás poden su tile y
    // cierren el peer. Sin esto el roster quedaba con un participante fantasma (leave solo emitía
    // StateChanged, que no poda participantes; el Remove/expulsar sí lo emitía y por eso ese sí limpiaba).
    emitter.emitToMeeting({
      tenantId,
      meetingId: parsed.data.meetingId,
      event: MeetingSocketEvents.ParticipantChanged,
      envelope: envelope({
        meetingId: parsed.data.meetingId,
        participant: result.value.participant,
        sequence: 0,
      }),
    });
    await socket.leave(`t:${tenantId}:m:${parsed.data.meetingId}`);
    if (result.value.conversationId) {
      await socket.leave(`t:${tenantId}:c:${result.value.conversationId}`);
    }
    await closeSfuForParticipant(parsed.data.meetingId, userId);
    const meetingAfterLeave = await container.meetings.findById(tenantId, parsed.data.meetingId);
    const snap = meetingAfterLeave?.toSnapshot();
    if (snap?.status === 'Ended') {
      await container.sfu
        .closeMeeting(parsed.data.meetingId)
        .catch((err: unknown) =>
          logger.warn({ err, meetingId: parsed.data.meetingId }, 'sfu closeMeeting failed'),
        );
      // El meeting termino en cascada (host se fue sin cohost disponible) —
      // TODOS los que estaban dentro quedan Left en la misma llamada de
      // dominio, sin que sus propios sockets disparen un Leave individual.
      await clearMeetingBusyFor(
        container,
        tenantId,
        parsed.data.meetingId,
        snap.participants.map((p) => p.userId),
      );
    } else {
      await clearMeetingBusyFor(container, tenantId, parsed.data.meetingId, [userId]);
    }
    emitter.emitToMeeting({
      tenantId,
      meetingId: parsed.data.meetingId,
      event: MeetingSocketEvents.StateChanged,
      envelope: envelope({
        meetingId: parsed.data.meetingId,
        status: snap?.status ?? 'Live',
        isLocked: snap?.isLocked ?? false,
        hostUserId: snap?.hostUserId ?? userId,
        sequence: 0,
      }),
    });
    if (snap?.status === 'Ended') {
      // Terminó en cascada: que la LISTA de participantes/invitados mueva la fila a "past".
      await emitMeetingEndedToLists(
        container,
        emitter,
        tenantId,
        parsed.data.meetingId,
        snap.participants.map((p) => p.userId),
      );
    }
  });

  socket.on(MeetingSocketEvents.Admit, async (...args: unknown[]) => {
    const raw = args[0];
    const ack =
      typeof args[1] === 'function' ? (args[1] as (r: SocketAck<MeetingParticipantDto>) => void) : undefined;
    const parsed = AdmitPayloadSchema.safeParse(raw);
    if (!parsed.success) {
      ack?.({ ok: false, code: 'Meeting.BadPayload', message: parsed.error.message });
      return;
    }
    if (!(await requireHostPermission(ack))) return;
    const result = await admitParticipant(
      {
        tenantId,
        correlationId: socket.id,
        meetingId: parsed.data.meetingId,
        hostUserId: userId,
        targetUserId: parsed.data.targetUserId,
      },
      container,
    );
    if (!result.isSuccess) {
      ack?.({ ok: false, code: result.error.code, message: result.error.message });
      return;
    }
    ack?.({ ok: true, value: result.value.participant });
    if (result.value.conversationId) {
      // El admitido no dispara este handler (lo hace el host) — sus sockets
      // no llamaron socket.join, hay que meterlos a la room desde aca.
      io.in(`t:${tenantId}:u:${parsed.data.targetUserId}`).socketsJoin(
        `t:${tenantId}:c:${result.value.conversationId}`,
      );
    }
    // El admitido recien pasa a Joined de verdad aca (antes estaba Waiting,
    // no contaba como busy).
    await container.presence
      .markBusy({
        tenantId,
        userId: parsed.data.targetUserId,
        sourceId: parsed.data.meetingId,
        kind: 'Meeting',
        leaseSeconds: MEETING_BUSY_LEASE_SECONDS,
      })
      .catch((err: unknown) => logger.warn({ err }, 'presence markBusy (meeting admit) failed'));
    emitter.emitToMeeting({
      tenantId,
      meetingId: parsed.data.meetingId,
      event: MeetingSocketEvents.ParticipantChanged,
      envelope: envelope({
        meetingId: parsed.data.meetingId,
        participant: result.value.participant,
        sequence: 0,
      }),
    });
    // El admitido recibe su snapshot completo (conversationId + participantes +
    // estrategia): al salir de la sala de espera no hay otro modo de conocerlos.
    emitter.emitToUser({
      tenantId,
      userId: parsed.data.targetUserId,
      event: MeetingSocketEvents.Snapshot,
      envelope: envelope(result.value.snapshot),
    });
  });

  socket.on(MeetingSocketEvents.Remove, async (...args: unknown[]) => {
    const raw = args[0];
    const ack =
      typeof args[1] === 'function' ? (args[1] as (r: SocketAck<MeetingParticipantDto>) => void) : undefined;
    const parsed = RemovePayloadSchema.safeParse(raw);
    if (!parsed.success) {
      ack?.({ ok: false, code: 'Meeting.BadPayload', message: parsed.error.message });
      return;
    }
    if (!(await requireHostPermission(ack))) return;
    const result = await removeParticipant(
      {
        tenantId,
        correlationId: socket.id,
        meetingId: parsed.data.meetingId,
        hostUserId: userId,
        targetUserId: parsed.data.targetUserId,
      },
      container,
    );
    if (!result.isSuccess) {
      ack?.({ ok: false, code: result.error.code, message: result.error.message });
      return;
    }
    ack?.({ ok: true, value: result.value.participant });
    const removedDto: MeetingYouRemovedDto = { meetingId: parsed.data.meetingId, reason: 'RemovedByHost' };
    emitter.emitToUser({
      tenantId,
      userId: parsed.data.targetUserId,
      event: MeetingSocketEvents.YouRemoved,
      envelope: envelope(removedDto),
    });
    if (result.value.conversationId) {
      // El expulsado puede tener otros sockets conectados (otra pestana,
      // otro dispositivo) — sacarlos a todos de la room de chat del meeting,
      // mismo criterio que el kick de grupos (Fase 7).
      io.in(`t:${tenantId}:u:${parsed.data.targetUserId}`).socketsLeave(
        `t:${tenantId}:c:${result.value.conversationId}`,
      );
    }
    await clearMeetingBusyFor(container, tenantId, parsed.data.meetingId, [parsed.data.targetUserId]);
    emitter.emitToUser({
      tenantId,
      userId: parsed.data.targetUserId,
      event: MeetingSocketEvents.StateChanged,
      envelope: envelope({
        meetingId: parsed.data.meetingId,
        status: 'Live' as const,
        isLocked: false,
        hostUserId: userId,
        sequence: 0,
      }),
    });
    emitter.emitToMeeting({
      tenantId,
      meetingId: parsed.data.meetingId,
      event: MeetingSocketEvents.ParticipantChanged,
      envelope: envelope({
        meetingId: parsed.data.meetingId,
        participant: result.value.participant,
        sequence: 0,
      }),
    });
  });

  socket.on(MeetingSocketEvents.Lock, async (...args: unknown[]) => {
    const ack = typeof args[1] === 'function' ? (args[1] as (r: SocketAck<{ isLocked: boolean }>) => void) : undefined;
    const parsed = LockPayloadSchema.safeParse(args[0]);
    if (!parsed.success) {
      ack?.({ ok: false, code: 'Meeting.BadPayload', message: parsed.error.message });
      return;
    }
    if (!(await requireHostPermission(ack))) return;
    const result = await setMeetingLocked(
      {
        tenantId,
        correlationId: socket.id,
        meetingId: parsed.data.meetingId,
        hostUserId: userId,
        locked: parsed.data.locked,
      },
      container,
    );
    if (!result.isSuccess) {
      ack?.({ ok: false, code: result.error.code, message: result.error.message });
      return;
    }
    ack?.({ ok: true, value: result.value });
    emitter.emitToMeeting({
      tenantId,
      meetingId: parsed.data.meetingId,
      event: MeetingSocketEvents.StateChanged,
      envelope: envelope({
        meetingId: parsed.data.meetingId,
        status: 'Live' as const,
        isLocked: result.value.isLocked,
        hostUserId: userId,
        sequence: 0,
      }),
    });
  });

  socket.on(MeetingSocketEvents.MuteAll, async (...args: unknown[]) => {
    const ack = typeof args[1] === 'function' ? (args[1] as (r: SocketAck<{ affected: number }>) => void) : undefined;
    const parsed = LeaveMeetingPayloadSchema.safeParse(args[0]);
    if (!parsed.success) {
      ack?.({ ok: false, code: 'Meeting.BadPayload', message: parsed.error.message });
      return;
    }
    if (!(await requireHostPermission(ack))) return;
    const result = await muteAllInMeeting(
      { tenantId, meetingId: parsed.data.meetingId, hostUserId: userId, correlationId: socket.id },
      container,
    );
    if (!result.isSuccess) {
      ack?.({ ok: false, code: result.error.code, message: result.error.message });
      return;
    }
    ack?.({ ok: true, value: result.value });
    emitter.emitToMeeting({
      tenantId,
      meetingId: parsed.data.meetingId,
      event: MeetingSocketEvents.MutedByHost,
      envelope: envelope({ meetingId: parsed.data.meetingId, byUserId: userId }),
    });
  });

  socket.on(MeetingSocketEvents.TransferHost, async (...args: unknown[]) => {
    const ack = typeof args[1] === 'function' ? (args[1] as (r: SocketAck<{ hostUserId: string }>) => void) : undefined;
    const parsed = TransferHostPayloadSchema.safeParse(args[0]);
    if (!parsed.success) {
      ack?.({ ok: false, code: 'Meeting.BadPayload', message: parsed.error.message });
      return;
    }
    if (!(await requireHostPermission(ack))) return;
    const result = await transferMeetingHost(
      {
        tenantId,
        correlationId: socket.id,
        meetingId: parsed.data.meetingId,
        currentHostUserId: userId,
        newHostUserId: parsed.data.newHostUserId,
      },
      container,
    );
    if (!result.isSuccess) {
      ack?.({ ok: false, code: result.error.code, message: result.error.message });
      return;
    }
    ack?.({ ok: true, value: { hostUserId: result.value.hostUserId } });
    // Orden INV-07 §13.1: roles de los dos afectados → host.changed → estado real (lock incluido).
    for (const participant of result.value.changed) {
      emitter.emitToMeeting({
        tenantId,
        meetingId: parsed.data.meetingId,
        event: MeetingSocketEvents.ParticipantChanged,
        envelope: envelope({ meetingId: parsed.data.meetingId, participant, sequence: 0 }),
      });
    }
    const hostChanged: MeetingHostChangedDto = {
      meetingId: parsed.data.meetingId,
      previousHostUserId: result.value.previousHostUserId,
      newHostUserId: result.value.hostUserId,
      reason: 'Transfer',
    };
    emitter.emitToMeeting({
      tenantId,
      meetingId: parsed.data.meetingId,
      event: MeetingSocketEvents.HostChanged,
      envelope: envelope(hostChanged),
    });
    emitter.emitToMeeting({
      tenantId,
      meetingId: parsed.data.meetingId,
      event: MeetingSocketEvents.StateChanged,
      envelope: envelope({
        meetingId: parsed.data.meetingId,
        status: 'Live' as const,
        isLocked: result.value.isLocked,
        hostUserId: result.value.hostUserId,
        sequence: 0,
      }),
    });
  });

  socket.on(MeetingSocketEvents.DenyParticipant, async (...args: unknown[]) => {
    const raw = args[0];
    const ack = typeof args[1] === 'function' ? (args[1] as (r: SocketAck<void>) => void) : undefined;
    const parsed = DenyParticipantPayloadSchema.safeParse(raw);
    if (!parsed.success) {
      ack?.({ ok: false, code: 'Meeting.BadPayload', message: parsed.error.message });
      return;
    }
    if (!(await requireHostPermission(ack))) return;
    const result = await denyWaitingRoomParticipant(
      {
        tenantId,
        correlationId: socket.id,
        meetingId: parsed.data.meetingId,
        hostUserId: userId,
        targetUserId: parsed.data.targetUserId,
      },
      { ...container, emitter },
    );
    if (!result.isSuccess) {
      ack?.({ ok: false, code: result.error.code, message: result.error.message });
      return;
    }
    ack?.({ ok: true, value: undefined });
  });

  socket.on(MeetingSocketEvents.CancelMeeting, async (...args: unknown[]) => {
    const raw = args[0];
    const ack = typeof args[1] === 'function' ? (args[1] as (r: SocketAck<void>) => void) : undefined;
    const parsed = CancelMeetingPayloadSchema.safeParse(raw);
    if (!parsed.success) {
      ack?.({ ok: false, code: 'Meeting.BadPayload', message: parsed.error.message });
      return;
    }
    if (!(await requireHostPermission(ack))) return;
    const result = await cancelMeeting(
      {
        tenantId,
        correlationId: socket.id,
        meetingId: parsed.data.meetingId,
        hostUserId: userId,
        ...(parsed.data.reason !== undefined ? { reason: parsed.data.reason } : {}),
      },
      { ...container, emitter },
    );
    if (!result.isSuccess) {
      ack?.({ ok: false, code: result.error.code, message: result.error.message });
      return;
    }
    ack?.({ ok: true, value: undefined });
  });

  socket.on(MeetingSocketEvents.RescheduleMeeting, async (...args: unknown[]) => {
    const raw = args[0];
    const ack = typeof args[1] === 'function' ? (args[1] as (r: SocketAck<void>) => void) : undefined;
    const parsed = RescheduleMeetingPayloadSchema.safeParse(raw);
    if (!parsed.success) {
      ack?.({ ok: false, code: 'Meeting.BadPayload', message: parsed.error.message });
      return;
    }
    if (!(await requireHostPermission(ack))) return;
    const result = await rescheduleMeeting(
      {
        tenantId,
        correlationId: socket.id,
        meetingId: parsed.data.meetingId,
        hostUserId: userId,
        newScheduledForUtc: parsed.data.newScheduledForUtc,
      },
      { ...container, emitter },
    );
    if (!result.isSuccess) {
      ack?.({ ok: false, code: result.error.code, message: result.error.message });
      return;
    }
    ack?.({ ok: true, value: undefined });
  });

  socket.on(MeetingSocketEvents.PromoteCohost, async (...args: unknown[]) => {
    const raw = args[0];
    const ack =
      typeof args[1] === 'function' ? (args[1] as (r: SocketAck<MeetingParticipantDto>) => void) : undefined;
    const parsed = PromoteCohostPayloadSchema.safeParse(raw);
    if (!parsed.success) {
      ack?.({ ok: false, code: 'Meeting.BadPayload', message: parsed.error.message });
      return;
    }
    if (!(await requireHostPermission(ack))) return;
    const result = await promoteParticipantToCohost(
      {
        tenantId,
        correlationId: socket.id,
        meetingId: parsed.data.meetingId,
        hostUserId: userId,
        targetUserId: parsed.data.targetUserId,
      },
      { ...container, emitter },
    );
    if (!result.isSuccess) {
      ack?.({ ok: false, code: result.error.code, message: result.error.message });
      return;
    }
    ack?.({ ok: true, value: result.value.participant });
  });

  socket.on(MeetingSocketEvents.DemoteCohost, async (...args: unknown[]) => {
    const raw = args[0];
    const ack =
      typeof args[1] === 'function' ? (args[1] as (r: SocketAck<MeetingParticipantDto>) => void) : undefined;
    const parsed = DemoteCohostPayloadSchema.safeParse(raw);
    if (!parsed.success) {
      ack?.({ ok: false, code: 'Meeting.BadPayload', message: parsed.error.message });
      return;
    }
    if (!(await requireHostPermission(ack))) return;
    const result = await demoteCohostToAttendee(
      {
        tenantId,
        correlationId: socket.id,
        meetingId: parsed.data.meetingId,
        hostUserId: userId,
        targetUserId: parsed.data.targetUserId,
      },
      { ...container, emitter },
    );
    if (!result.isSuccess) {
      ack?.({ ok: false, code: result.error.code, message: result.error.message });
      return;
    }
    ack?.({ ok: true, value: result.value.participant });
  });

  socket.on(MeetingSocketEvents.Signal, async (...args: unknown[]) => {
    const parsed = MeetingSignalPayloadSchema.safeParse(args[0]);
    if (!parsed.success) return;
    const result = await relayMeetingSignal(
      {
        tenantId,
        meetingId: parsed.data.meetingId,
        fromUserId: userId,
        targetPeerUserId: parsed.data.targetPeerUserId,
        kind: parsed.data.kind,
        data: parsed.data.data,
      },
      container,
    );
    if (!result.isSuccess) return;
    // Dirigida al socket que está en la reunión; si no se conoce (pestaña vieja), al room del usuario.
    const targetSocketId = registry.find(tenantId, parsed.data.meetingId, result.value.targetUserId);
    if (targetSocketId) {
      emitter.emitToSocket({
        socketId: targetSocketId,
        event: MeetingSocketEvents.SignalFrom,
        envelope: envelope(result.value.signal),
      });
      return;
    }
    emitter.emitToUser({
      tenantId,
      userId: result.value.targetUserId,
      event: MeetingSocketEvents.SignalFrom,
      envelope: envelope(result.value.signal),
    });
  });

  socket.on(MeetingSocketEvents.MediaStatus, async (...args: unknown[]) => {
    const parsed = MeetingMediaStatusPayloadSchema.safeParse(args[0]);
    if (!parsed.success) return;
    const result = await updateMediaStatus(
      {
        scope: 'Meeting',
        scopeId: parsed.data.meetingId,
        tenantId,
        userId,
        audioEnabled: parsed.data.audioEnabled,
        videoEnabled: parsed.data.videoEnabled,
        screenSharing: parsed.data.screenSharing,
        correlationId: socket.id,
      },
      container,
    );
    if (!result.isSuccess || result.value.scope !== 'Meeting') return;
    emitter.emitToMeeting({
      tenantId,
      meetingId: parsed.data.meetingId,
      event: MeetingSocketEvents.ParticipantChanged,
      envelope: envelope({ meetingId: parsed.data.meetingId, participant: result.value.participant, sequence: 0 }),
    });
  });

  // F4 (T-48): nivel de conexion propio. Rate limit por socket derivado del intervalo de muestreo de la
  // politica (mitad del intervalo): un cliente roto no puede inundar el log ni la sala.
  let lastQualityReportAtMs = 0;
  socket.on(MeetingSocketEvents.ConnectionQuality, async (...args: unknown[]) => {
    const parsed = MeetingConnectionQualityPayloadSchema.safeParse(args[0]);
    if (!parsed.success) return;
    const effective = await container.effectiveSettings.resolve(tenantId).catch(() => null);
    const minGapMs = ((effective?.media.quality.sampleIntervalSeconds ?? TECHNICAL_LIMITS.qualitySampleIntervalSeconds.fallback) * 1000) / 2;
    const nowMs = Date.now();
    if (nowMs - lastQualityReportAtMs < minGapMs) return;
    lastQualityReportAtMs = nowMs;
    await reportConnectionQuality(
      { scope: 'Meeting', scopeId: parsed.data.meetingId, tenantId, userId, quality: parsed.data.quality, metrics: parsed.data.metrics, correlationId: socket.id },
      { ...container, emitter },
    );
  });

  // F5 (D-49): mano con ack (la UI hace rollback si falla) y timestamp para la cola del host.
  socket.on(MeetingSocketEvents.RaiseHand, async (...args: unknown[]) => {
    const ack = typeof args[1] === 'function' ? (args[1] as (r: SocketAck<MeetingRaiseHandAckDto>) => void) : undefined;
    const parsed = MeetingRaiseHandPayloadSchema.safeParse(args[0]);
    if (!parsed.success) {
      ack?.({ ok: false, code: 'Meeting.BadPayload', message: parsed.error.message });
      return;
    }
    const result = await updateRaiseHand(
      { tenantId, meetingId: parsed.data.meetingId, actorUserId: userId, raised: parsed.data.raised },
      container,
    );
    if (!result.isSuccess) {
      ack?.({ ok: false, code: result.error.code, message: result.error.message });
      return;
    }
    ack?.({ ok: true, value: { handRaised: result.value.handRaised, handRaisedAtUtc: result.value.handRaisedAtUtc } });
    emitter.emitToMeeting({
      tenantId,
      meetingId: parsed.data.meetingId,
      event: MeetingSocketEvents.ParticipantChanged,
      envelope: envelope({ meetingId: parsed.data.meetingId, participant: result.value, sequence: 0 }),
    });
  });

  // F5 (D-49): host/cohost baja una mano; el afectado recibe `meeting.hand.lowered_by_host` en su room de usuario.
  socket.on(MeetingSocketEvents.LowerHand, async (...args: unknown[]) => {
    const ack =
      typeof args[1] === 'function' ? (args[1] as (r: SocketAck<{ participant: MeetingParticipantDto }>) => void) : undefined;
    const parsed = LowerHandPayloadSchema.safeParse(args[0]);
    if (!parsed.success) {
      ack?.({ ok: false, code: 'Meeting.BadPayload', message: parsed.error.message });
      return;
    }
    if (!(await requireHostPermission(ack))) return;
    const result = await lowerParticipantHand(
      { tenantId, meetingId: parsed.data.meetingId, hostUserId: userId, targetUserId: parsed.data.targetUserId, correlationId: socket.id },
      container,
    );
    if (!result.isSuccess) {
      ack?.({ ok: false, code: result.error.code, message: result.error.message });
      return;
    }
    ack?.({ ok: true, value: { participant: result.value.participant } });
    if (!result.value.changed) return;
    emitter.emitToMeeting({
      tenantId,
      meetingId: parsed.data.meetingId,
      event: MeetingSocketEvents.ParticipantChanged,
      envelope: envelope({ meetingId: parsed.data.meetingId, participant: result.value.participant, sequence: 0 }),
    });
    const lowered: MeetingHandLoweredByHostDto = { meetingId: parsed.data.meetingId, byUserId: userId, all: false };
    emitter.emitToUser({ tenantId, userId: parsed.data.targetUserId, event: MeetingSocketEvents.HandLoweredByHost, envelope: envelope(lowered) });
  });

  socket.on(MeetingSocketEvents.LowerAllHands, async (...args: unknown[]) => {
    const ack = typeof args[1] === 'function' ? (args[1] as (r: SocketAck<{ lowered: number }>) => void) : undefined;
    const parsed = LowerAllHandsPayloadSchema.safeParse(args[0]);
    if (!parsed.success) {
      ack?.({ ok: false, code: 'Meeting.BadPayload', message: parsed.error.message });
      return;
    }
    if (!(await requireHostPermission(ack))) return;
    const result = await lowerAllHands(
      { tenantId, meetingId: parsed.data.meetingId, hostUserId: userId, correlationId: socket.id },
      container,
    );
    if (!result.isSuccess) {
      ack?.({ ok: false, code: result.error.code, message: result.error.message });
      return;
    }
    ack?.({ ok: true, value: { lowered: result.value.participants.length } });
    const lowered: MeetingHandLoweredByHostDto = { meetingId: parsed.data.meetingId, byUserId: userId, all: true };
    for (const participant of result.value.participants) {
      emitter.emitToMeeting({
        tenantId,
        meetingId: parsed.data.meetingId,
        event: MeetingSocketEvents.ParticipantChanged,
        envelope: envelope({ meetingId: parsed.data.meetingId, participant, sequence: 0 }),
      });
      emitter.emitToUser({ tenantId, userId: participant.userId, event: MeetingSocketEvents.HandLoweredByHost, envelope: envelope(lowered) });
    }
  });

  // F5 (D-50): reaccion efimera con ack por codigo; la difusion volatil a la sala la hace el servicio.
  socket.on(MeetingSocketEvents.Reaction, async (...args: unknown[]) => {
    const ack = typeof args[1] === 'function' ? (args[1] as (r: SocketAck<null>) => void) : undefined;
    const parsed = MeetingReactionPayloadSchema.safeParse(args[0]);
    if (!parsed.success) {
      ack?.({ ok: false, code: 'Meeting.BadPayload', message: parsed.error.message });
      return;
    }
    // Pre-filtro barato por el registro de sockets del meeting (en vivo `socket.rooms` no es fiable con
    // el adapter de Redis); el caso de uso vuelve a comprobar que sea participante Joined en BD.
    if (registry.find(tenantId, parsed.data.meetingId, userId) === undefined) {
      ack?.({ ok: false, code: 'Meeting.NotParticipant', message: 'Not a joined participant.' });
      return;
    }
    const result = await sendMeetingReaction(
      { tenantId, meetingId: parsed.data.meetingId, userId, emoji: parsed.data.emoji, correlationId: socket.id },
      { ...container, emitter },
    );
    if (!result.isSuccess) {
      ack?.({ ok: false, code: result.error.code, message: result.error.message });
      return;
    }
    ack?.({ ok: true, value: null });
  });

  // F5 (T-69): linea de tiempo de hablantes. Sin difusion (la UI detecta al hablante en local); solo
  // el MeetingEventLog, con el tope por segundo de la politica y sin repetir el mismo estado.
  let lastSpeaking: boolean | null = null;
  socket.on(MeetingSocketEvents.Speaking, async (...args: unknown[]) => {
    const parsed = MeetingSpeakingPayloadSchema.safeParse(args[0]);
    if (!parsed.success) return;
    if (registry.find(tenantId, parsed.data.meetingId, userId) === undefined) return;
    if (parsed.data.speaking === lastSpeaking) return;
    const effective = await container.effectiveSettings.resolve(tenantId).catch(() => null);
    const allowed = await container.speakingThrottle.allow(
      { tenantId, meetingId: parsed.data.meetingId, userId },
      effective?.media.speaking.maxPerSecond,
    );
    if (!allowed) return;
    lastSpeaking = parsed.data.speaking;
    await recordMeetingEvent(container, {
      tenantId,
      meetingId: parsed.data.meetingId,
      type: parsed.data.speaking ? MeetingEventType.SpeakerStarted : MeetingEventType.SpeakerStopped,
      actorUserId: userId,
      correlationId: socket.id,
    });
  });

  socket.on(MeetingSocketEvents.AttachRecording, async (...args: unknown[]) => {
    const raw = args[0];
    const ack =
      typeof args[1] === 'function'
        ? (args[1] as (r: SocketAck<{ meetingId: string; recordingFileId: string }>) => void)
        : undefined;
    const parsed = AttachMeetingRecordingPayloadSchema.safeParse(raw);
    if (!parsed.success) {
      ack?.({ ok: false, code: 'Meeting.BadPayload', message: parsed.error.message });
      return;
    }
    // A6/A5.4 — grabar es una accion con permiso propio y hasta ahora no se exigia en ningun
    // sitio: cualquier participante podia grabar. `RespondRecordingConsent` queda FUERA a
    // proposito — quien consiente no graba, y exigirselo bloquearia la grabacion entera.
    const attachRecordingPermCheck = await checkPermission(
      principal,
      CommunicationPermissions.MeetingRecord,
      container.userPermissions,
    );
    if (!attachRecordingPermCheck.allowed) {
      ack?.({ ok: false, code: attachRecordingPermCheck.code, message: attachRecordingPermCheck.message });
      return;
    }
    const result = await attachRecording(
      'Meeting',
      {
        tenantId,
        correlationId: socket.id,
        clientKey: parsed.data.clientKey,
        scopeId: parsed.data.meetingId,
        actorUserId: userId,
        fileId: parsed.data.fileId,
      },
      { ...container, emitter },
    );
    if (!result.isSuccess) {
      ack?.({ ok: false, code: result.error.code, message: result.error.message });
      return;
    }
    ack?.({ ok: true, value: result.value });
  });

  // ---------------------------------------------------------------------
  // Recording consent (Fase Backend 3) — meeting.recording.start_request pide
  // consentimiento (Meeting.requestRecording, Idle -> Requesting), NO arranca
  // la grabacion todavia; el arranque real ocurre dentro de
  // respond-meeting-recording-consent.ts (auto-invoke) o del timeout scheduler
  // cuando la policy del tenant se satisface — por eso no hay un handler
  // socket separado para "start": nunca lo dispara directamente el cliente.
  // ---------------------------------------------------------------------

  socket.on(MeetingSocketEvents.RequestRecording, async (...args: unknown[]) => {
    const raw = args[0];
    const ack =
      typeof args[1] === 'function'
        ? (args[1] as (
            r: SocketAck<{
              meetingId: string;
              participantUserIds: readonly string[];
              requestedAtUtc: string;
            }>,
          ) => void)
        : undefined;
    const parsed = RequestMeetingRecordingPayloadSchema.safeParse(raw);
    if (!parsed.success) {
      ack?.({ ok: false, code: 'Meeting.BadPayload', message: parsed.error.message });
      return;
    }
    // A6/A5.4 — grabar es una accion con permiso propio y hasta ahora no se exigia en ningun
    // sitio: cualquier participante podia grabar. `RespondRecordingConsent` queda FUERA a
    // proposito — quien consiente no graba, y exigirselo bloquearia la grabacion entera.
    const requestRecordingPermCheck = await checkPermission(
      principal,
      CommunicationPermissions.MeetingRecord,
      container.userPermissions,
    );
    if (!requestRecordingPermCheck.allowed) {
      ack?.({ ok: false, code: requestRecordingPermCheck.code, message: requestRecordingPermCheck.message });
      return;
    }
    const result = await requestRecording(
      'Meeting',
      {
        tenantId,
        correlationId: socket.id,
        clientKey: parsed.data.clientKey,
        scopeId: parsed.data.meetingId,
        actorUserId: userId,
      },
      { ...container, emitter },
    );
    if (!result.isSuccess) {
      ack?.({ ok: false, code: result.error.code, message: result.error.message });
      return;
    }
    ack?.({ ok: true, value: result.value });
  });

  socket.on(MeetingSocketEvents.RespondRecordingConsent, async (...args: unknown[]) => {
    const raw = args[0];
    const ack =
      typeof args[1] === 'function'
        ? (args[1] as (r: SocketAck<{ response: RecordingConsentEntryStatus }>) => void)
        : undefined;
    const parsed = RespondMeetingRecordingConsentPayloadSchema.safeParse(raw);
    if (!parsed.success) {
      ack?.({ ok: false, code: 'Meeting.BadPayload', message: parsed.error.message });
      return;
    }
    const result = await respondRecordingConsent(
      'Meeting',
      {
        tenantId,
        correlationId: socket.id,
        scopeId: parsed.data.meetingId,
        actorUserId: userId,
        response: parsed.data.response,
      },
      { ...container, emitter },
    );
    if (!result.isSuccess) {
      ack?.({ ok: false, code: result.error.code, message: result.error.message });
      return;
    }
    ack?.({ ok: true, value: result.value });
  });

  socket.on(MeetingSocketEvents.StopRecording, async (...args: unknown[]) => {
    const raw = args[0];
    const ack =
      typeof args[1] === 'function'
        ? (args[1] as (r: SocketAck<{ meetingId: string; elapsedSeconds: number }>) => void)
        : undefined;
    const parsed = StopMeetingRecordingPayloadSchema.safeParse(raw);
    if (!parsed.success) {
      ack?.({ ok: false, code: 'Meeting.BadPayload', message: parsed.error.message });
      return;
    }
    // A6/A5.4 — grabar es una accion con permiso propio y hasta ahora no se exigia en ningun
    // sitio: cualquier participante podia grabar. `RespondRecordingConsent` queda FUERA a
    // proposito — quien consiente no graba, y exigirselo bloquearia la grabacion entera.
    const stopRecordingPermCheck = await checkPermission(
      principal,
      CommunicationPermissions.MeetingRecord,
      container.userPermissions,
    );
    if (!stopRecordingPermCheck.allowed) {
      ack?.({ ok: false, code: stopRecordingPermCheck.code, message: stopRecordingPermCheck.message });
      return;
    }
    const result = await stopRecording(
      'Meeting',
      {
        tenantId,
        correlationId: socket.id,
        clientKey: parsed.data.clientKey,
        scopeId: parsed.data.meetingId,
        actorUserId: userId,
      },
      { ...container, emitter },
    );
    if (!result.isSuccess) {
      ack?.({ ok: false, code: result.error.code, message: result.error.message });
      return;
    }
    ack?.({ ok: true, value: result.value });
  });

  // ---------------------------------------------------------------------
  // Chat dentro del meeting (Fase 8) — reusa integramente sendMessage/
  // editMessage/deleteMessage/markMessagesRead (mismo motor que chat 1:1 y
  // grupos, incluye adjuntos via AttachmentTracking sin cambios). La unica
  // diferencia real es la autorizacion: en vez de `communication.chat.reply`
  // se exige ser participante activo del chat del meeting (sincronizado con
  // quien esta Joined — ver ensure-meeting-conversation.ts), asi que un
  // usuario con `communication.meeting.join` pero sin permisos de chat
  // general igual puede escribir en el chat del meeting al que ya entro.
  // ---------------------------------------------------------------------

  const resolveMeetingConversationId = async (meetingId: string): Promise<string | null> => {
    const conversation = await container.conversations.findByUniquenessKey(
      tenantId,
      computeMeetingUniquenessKey(meetingId),
    );
    return conversation?.id ?? null;
  };

  socket.on(MeetingSocketEvents.ChatSend, async (...args: unknown[]) => {
    const raw = args[0];
    const ack =
      typeof args[1] === 'function'
        ? (args[1] as (r: SocketAck<{ message: MessageDto }>) => void)
        : undefined;
    const parsed = MeetingChatSendPayloadSchema.safeParse(raw);
    if (!parsed.success) {
      ack?.({ ok: false, code: 'Meeting.BadPayload', message: parsed.error.message });
      return;
    }
    const conversationId = await resolveMeetingConversationId(parsed.data.meetingId);
    if (!conversationId) {
      ack?.({
        ok: false,
        code: 'Meeting.Chat.NotStarted',
        message: 'Meeting chat has not been created yet.',
      });
      return;
    }
    // F8a: permiso del chat para cuentas (INV-10 H-7), guests solo texto (D-14) y topes de adjuntos (D-15).
    const policy = await authorizeMeetingChatSend(
      { principal: { ...principal, tenantId }, conversationId, hasAttachment: Boolean(parsed.data.attachmentFileId) },
      container,
    );
    if (!policy.isSuccess) {
      ack?.({ ok: false, code: policy.error.code, message: policy.error.message });
      return;
    }
    const allowed = await container.rateLimiter.allow({
      scope: CommunicationRateLimitPolicyNames.MeetingChatSend,
      tenantId,
      userId,
      maxPerWindow: await container.tierAwareQuota.resolveMaxPerWindow(
        tenantId,
        config.rateLimit.meetingChatSend.maxPerWindow,
      ),
      windowSeconds: config.rateLimit.meetingChatSend.windowSeconds,
    });
    if (!allowed) {
      ack?.({ ok: false, code: 'Meeting.Chat.RateLimited', message: 'Too many messages, slow down.' });
      return;
    }
    const result = await sendMessage(
      {
        tenantId,
        correlationId: socket.id,
        clientKey: parsed.data.clientKey,
        conversationId,
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
      conversationId,
      event: MeetingSocketEvents.ChatMessageNew,
      envelope: envelope(result.value.message),
    });
    // F8a (T-10): evento único del chat con `conversationKind: 'Meeting'` y `meetingId`; el de la sala queda de fachada.
    emitter.emitToConversation({
      tenantId,
      conversationId,
      event: ChatSocketEvents.MessageNew,
      envelope: envelope(result.value.message),
    });
  });

  socket.on(MeetingSocketEvents.ChatEdit, async (...args: unknown[]) => {
    const raw = args[0];
    const ack =
      typeof args[1] === 'function'
        ? (args[1] as (r: SocketAck<{ edited: MessageEditedDto }>) => void)
        : undefined;
    const parsed = MeetingChatEditPayloadSchema.safeParse(raw);
    if (!parsed.success) {
      ack?.({ ok: false, code: 'Meeting.BadPayload', message: parsed.error.message });
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
      event: MeetingSocketEvents.ChatMessageEdited,
      envelope: envelope(result.value.edited),
    });
  });

  socket.on(MeetingSocketEvents.ChatDelete, async (...args: unknown[]) => {
    const raw = args[0];
    const ack =
      typeof args[1] === 'function'
        ? (args[1] as (r: SocketAck<{ deleted: MessageDeletedDto }>) => void)
        : undefined;
    const parsed = MeetingChatDeletePayloadSchema.safeParse(raw);
    if (!parsed.success) {
      ack?.({ ok: false, code: 'Meeting.BadPayload', message: parsed.error.message });
      return;
    }
    // Moderar (borrar mensaje ajeno) en un meeting es privilegio de host/cohost
    // — mismo criterio que ChatModerate en chat 1:1/grupos, adaptado a roles
    // de meeting en vez de un permiso Auth separado.
    const meeting = await container.meetings.findById(tenantId, parsed.data.meetingId);
    const canModerate = meeting ? meeting.hostUserId === userId || meeting.isCohost(userId) : false;
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
      event: MeetingSocketEvents.ChatMessageDeleted,
      envelope: envelope(result.value.deleted),
    });
  });

  socket.on(MeetingSocketEvents.ChatMarkRead, async (...args: unknown[]) => {
    const raw = args[0];
    const ack =
      typeof args[1] === 'function'
        ? (args[1] as (r: SocketAck<{ markedCount: number }>) => void)
        : undefined;
    const parsed = MeetingChatMarkReadPayloadSchema.safeParse(raw);
    if (!parsed.success) {
      ack?.({ ok: false, code: 'Meeting.BadPayload', message: parsed.error.message });
      return;
    }
    const conversationId = await resolveMeetingConversationId(parsed.data.meetingId);
    if (!conversationId) {
      ack?.({
        ok: false,
        code: 'Meeting.Chat.NotStarted',
        message: 'Meeting chat has not been created yet.',
      });
      return;
    }
    const result = await markMessagesRead(
      { tenantId, conversationId, userUserId: userId, lastReadMessageId: parsed.data.lastReadMessageId },
      container,
    );
    if (!result.isSuccess) {
      ack?.({ ok: false, code: result.error.code, message: result.error.message });
      return;
    }
    ack?.({ ok: true, value: { markedCount: result.value.markedCount } });
    emitter.emitToConversation({
      tenantId,
      conversationId,
      event: MeetingSocketEvents.ChatMessageRead,
      envelope: envelope(result.value.receipt),
    });
  });

  // ---------------------------------------------------------------------
  // SFU (mediasoup) — meetings con strategy 'Sfu' (>4 participantes). Cada
  // handler solo delega al use case (que valida "es participante Joined")
  // y a `container.sfu`; el server nunca inspecciona SDP/RTP, solo enruta.
  // ---------------------------------------------------------------------

  socket.on(MeetingSocketEvents.SfuGetRouterCapabilities, async (...args: unknown[]) => {
    const ack = typeof args[1] === 'function' ? (args[1] as (r: SocketAck<unknown>) => void) : undefined;
    const parsed = SfuRouterCapabilitiesPayloadSchema.safeParse(args[0]);
    if (!parsed.success) {
      ack?.({ ok: false, code: 'Meeting.BadPayload', message: parsed.error.message });
      return;
    }
    const result = await getSfuRouterCapabilities(
      { tenantId, meetingId: parsed.data.meetingId, userId },
      container,
    );
    if (!result.isSuccess) {
      ack?.({ ok: false, code: result.error.code, message: result.error.message });
      return;
    }
    ack?.({ ok: true, value: result.value });
  });

  socket.on(MeetingSocketEvents.SfuCreateTransport, async (...args: unknown[]) => {
    const ack = typeof args[1] === 'function' ? (args[1] as (r: SocketAck<unknown>) => void) : undefined;
    const parsed = SfuCreateTransportPayloadSchema.safeParse(args[0]);
    if (!parsed.success) {
      ack?.({ ok: false, code: 'Meeting.BadPayload', message: parsed.error.message });
      return;
    }
    const result = await createSfuTransport(
      { tenantId, meetingId: parsed.data.meetingId, userId, direction: parsed.data.direction },
      container,
    );
    if (!result.isSuccess) {
      ack?.({ ok: false, code: result.error.code, message: result.error.message });
      return;
    }
    ack?.({ ok: true, value: result.value });
    // El transport anterior (reconexión) se cerró con sus producers: los demás cierran esos consumers.
    for (const producerId of result.value.replacedProducerIds) {
      emitter.emitToMeeting({
        tenantId,
        meetingId: parsed.data.meetingId,
        event: MeetingSocketEvents.SfuProducerClosed,
        envelope: envelope({ meetingId: parsed.data.meetingId, userId, producerId } satisfies SfuProducerClosedDto),
      });
    }
  });

  socket.on(MeetingSocketEvents.SfuRestartIce, async (...args: unknown[]) => {
    const ack = typeof args[1] === 'function' ? (args[1] as (r: SocketAck<unknown>) => void) : undefined;
    const parsed = SfuRestartIcePayloadSchema.safeParse(args[0]);
    if (!parsed.success) {
      ack?.({ ok: false, code: 'Meeting.BadPayload', message: parsed.error.message });
      return;
    }
    const result = await restartSfuTransportIce(
      { tenantId, meetingId: parsed.data.meetingId, userId, transportId: parsed.data.transportId },
      container,
    );
    if (!result.isSuccess) {
      ack?.({ ok: false, code: result.error.code, message: result.error.message });
      return;
    }
    ack?.({ ok: true, value: result.value });
  });

  socket.on(MeetingSocketEvents.SfuConnectTransport, async (...args: unknown[]) => {
    const ack = typeof args[1] === 'function' ? (args[1] as (r: SocketAck<unknown>) => void) : undefined;
    const parsed = SfuConnectTransportPayloadSchema.safeParse(args[0]);
    if (!parsed.success) {
      ack?.({ ok: false, code: 'Meeting.BadPayload', message: parsed.error.message });
      return;
    }
    const result = await connectSfuTransport(
      {
        tenantId,
        meetingId: parsed.data.meetingId,
        userId,
        transportId: parsed.data.transportId,
        dtlsParameters: parsed.data.dtlsParameters as never,
      },
      container,
    );
    if (!result.isSuccess) {
      ack?.({ ok: false, code: result.error.code, message: result.error.message });
      return;
    }
    ack?.({ ok: true, value: {} });
  });

  socket.on(MeetingSocketEvents.SfuProduce, async (...args: unknown[]) => {
    const ack = typeof args[1] === 'function' ? (args[1] as (r: SocketAck<unknown>) => void) : undefined;
    const parsed = SfuProducePayloadSchema.safeParse(args[0]);
    if (!parsed.success) {
      ack?.({ ok: false, code: 'Meeting.BadPayload', message: parsed.error.message });
      return;
    }
    const result = await produceSfuMedia(
      {
        tenantId,
        meetingId: parsed.data.meetingId,
        userId,
        transportId: parsed.data.transportId,
        kind: parsed.data.kind,
        rtpParameters: parsed.data.rtpParameters as never,
        source: parsed.data.source,
      },
      container,
    );
    if (!result.isSuccess) {
      ack?.({ ok: false, code: result.error.code, message: result.error.message });
      return;
    }
    ack?.({ ok: true, value: result.value });
    const newProducer: SfuNewProducerDto = {
      meetingId: parsed.data.meetingId,
      userId,
      producerId: result.value.producerId,
      kind: parsed.data.kind,
      source: parsed.data.source,
    };
    socket
      .to(`t:${tenantId}:m:${parsed.data.meetingId}`)
      .emit(MeetingSocketEvents.SfuNewProducer, envelope(newProducer));
  });

  socket.on(MeetingSocketEvents.SfuConsume, async (...args: unknown[]) => {
    const ack = typeof args[1] === 'function' ? (args[1] as (r: SocketAck<unknown>) => void) : undefined;
    const parsed = SfuConsumePayloadSchema.safeParse(args[0]);
    if (!parsed.success) {
      ack?.({ ok: false, code: 'Meeting.BadPayload', message: parsed.error.message });
      return;
    }
    const result = await consumeSfuMedia(
      {
        tenantId,
        meetingId: parsed.data.meetingId,
        userId,
        transportId: parsed.data.transportId,
        producerId: parsed.data.producerId,
        rtpCapabilities: parsed.data.rtpCapabilities as never,
      },
      container,
    );
    if (!result.isSuccess) {
      ack?.({ ok: false, code: result.error.code, message: result.error.message });
      return;
    }
    ack?.({ ok: true, value: result.value });
  });

  socket.on(MeetingSocketEvents.SfuResumeConsumer, async (...args: unknown[]) => {
    const ack = typeof args[1] === 'function' ? (args[1] as (r: SocketAck<unknown>) => void) : undefined;
    const parsed = SfuResumeConsumerPayloadSchema.safeParse(args[0]);
    if (!parsed.success) {
      ack?.({ ok: false, code: 'Meeting.BadPayload', message: parsed.error.message });
      return;
    }
    const result = await resumeSfuConsumer(
      { tenantId, meetingId: parsed.data.meetingId, userId, consumerId: parsed.data.consumerId },
      container,
    );
    if (!result.isSuccess) {
      ack?.({ ok: false, code: result.error.code, message: result.error.message });
      return;
    }
    ack?.({ ok: true, value: {} });
  });

  socket.on(MeetingSocketEvents.SfuSetPreferredLayers, async (...args: unknown[]) => {
    const ack = typeof args[1] === 'function' ? (args[1] as (r: SocketAck<unknown>) => void) : undefined;
    const parsed = SfuSetPreferredLayersPayloadSchema.safeParse(args[0]);
    if (!parsed.success) {
      ack?.({ ok: false, code: 'Meeting.BadPayload', message: parsed.error.message });
      return;
    }
    const result = await setSfuConsumerPreferredLayers(
      {
        tenantId,
        meetingId: parsed.data.meetingId,
        userId,
        consumerId: parsed.data.consumerId,
        spatialLayer: parsed.data.spatialLayer,
        ...(parsed.data.temporalLayer !== undefined ? { temporalLayer: parsed.data.temporalLayer } : {}),
      },
      container,
    );
    if (!result.isSuccess) {
      ack?.({ ok: false, code: result.error.code, message: result.error.message });
      return;
    }
    ack?.({ ok: true, value: {} });
  });

  socket.on(MeetingSocketEvents.SfuListRemoteProducers, async (...args: unknown[]) => {
    const ack = typeof args[1] === 'function' ? (args[1] as (r: SocketAck<unknown>) => void) : undefined;
    const parsed = SfuListRemoteProducersPayloadSchema.safeParse(args[0]);
    if (!parsed.success) {
      ack?.({ ok: false, code: 'Meeting.BadPayload', message: parsed.error.message });
      return;
    }
    const result = await listSfuRemoteProducers(
      { tenantId, meetingId: parsed.data.meetingId, userId },
      container,
    );
    if (!result.isSuccess) {
      ack?.({ ok: false, code: result.error.code, message: result.error.message });
      return;
    }
    ack?.({ ok: true, value: result.value });
  });

  // Desconexion abrupta: si el socket seguia en algun room `t:{tenant}:m:{id}`
  // (se joinea en Meeting.Join), marcamos leave y avisamos a los demas
  // participantes — sin esto el roster se queda con un participant "Joined"
  // fantasma indefinidamente hasta que alguien lo saque a mano.
  // `disconnecting` y no `disconnect`: en Socket.IO 4 las rooms ya están vacías en `disconnect`
  // (la gracia nunca se agendaba y el participante quedaba Joined fantasma).
  socket.on('disconnecting', () => {
    const meetingRoomPrefix = `t:${tenantId}:m:`;
    const activeMeetingIds = [...socket.rooms].filter((room) => room.startsWith(meetingRoomPrefix));
    if (activeMeetingIds.length === 0) return;
    // Las rooms del socket desaparecen tras el `disconnect`: se capturan ahora y la gracia se lee
    // después (resolver con caché; si falla, cae al default técnico y nunca deja al participante colgado).
    void container.effectiveSettings
      .resolve(tenantId)
      .then((effective) => effective.meeting.disconnectGraceSeconds * 1000)
      .catch(() => TECHNICAL_LIMITS.disconnectGraceSeconds.fallback * 1000)
      .then((graceMs) => {
        for (const room of activeMeetingIds) {
          const meetingId = room.slice(meetingRoomPrefix.length);
          registry.unregister(tenantId, meetingId, userId, socket.id);
          // NO se saca al participante de una vez: se AGENDA su salida tras la gracia. Si el mismo usuario
          // reconecta (join/rejoin) dentro de la ventana, join/rejoin cancela este timer. Así un blip de red
          // o un cambio de pestaña del host no termina el meeting (host sin cohost → Meeting.end).
          const key = leaveKey(tenantId, meetingId, userId);
          const existing = pendingLeaves.get(key);
          if (existing) clearTimeout(existing);
          const timer = setTimeout(() => {
            pendingLeaves.delete(key);
            void performLeaveAfterGrace(container, emitter, tenantId, meetingId, userId);
          }, graceMs);
          pendingLeaves.set(key, timer);
        }
      });
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
