import { randomUUID } from 'node:crypto';
import { config } from '../../../infrastructure/config.js';
import { logger } from '../../../infrastructure/logger/logger.js';
import { checkPermission, CommunicationPermissions } from '../../../domain/shared/permissions.js';
import type { AppContainer } from '../../../infrastructure/container.js';
import type {
  CommunicationIoServer,
  CommunicationSocket,
} from '../../../infrastructure/socket/build-io.js';
import { SocketRealtimeEmitter } from '../../../infrastructure/socket/socket-realtime-emitter.js';
import { resolveDisplayName } from './resolve-display-name.js';
import {
  AdmitPayloadSchema,
  AttachMeetingRecordingPayloadSchema,
  CancelMeetingPayloadSchema,
  DemoteCohostPayloadSchema,
  DenyParticipantPayloadSchema,
  DominantSpeakerPayloadSchema,
  JoinMeetingPayloadSchema,
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
  SfuResumeConsumerPayloadSchema,
  SfuRouterCapabilitiesPayloadSchema,
  StopMeetingRecordingPayloadSchema,
  TransferHostPayloadSchema,
  type MeetingParticipantDto,
  type MessageDeletedDto,
  type MessageDto,
  type MessageEditedDto,
  type SfuNewProducerDto,
  type SfuProducerClosedDto,
} from '../../../contracts/socket/meeting-socket-events.js';
import type { SocketAck, SocketEnvelope } from '../../../contracts/socket/socket-envelope.js';
import { joinMeeting, type JoinMeetingResult } from '../../../application/use-cases/join-meeting.js';
import { leaveMeeting } from '../../../application/use-cases/leave-meeting.js';
import { relayMeetingSignal } from '../../../application/use-cases/relay-meeting-signal.js';
import { updateMeetingMediaStatus, updateRaiseHand } from '../../../application/use-cases/update-meeting-media-status.js';
import {
  admitParticipant,
  muteAllInMeeting,
  removeParticipant,
  setMeetingLocked,
  transferMeetingHost,
} from '../../../application/use-cases/meeting-host-actions.js';
import { attachMeetingRecording } from '../../../application/use-cases/attach-meeting-recording.js';
import { requestMeetingRecording } from '../../../application/use-cases/request-meeting-recording.js';
import { respondMeetingRecordingConsent } from '../../../application/use-cases/respond-meeting-recording-consent.js';
import { stopMeetingRecording } from '../../../application/use-cases/stop-meeting-recording.js';
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
  resumeSfuConsumer,
} from '../../../application/use-cases/sfu-signaling.js';
import { sendMessage } from '../../../application/use-cases/send-message.js';
import { editMessage } from '../../../application/use-cases/edit-message.js';
import { deleteMessage } from '../../../application/use-cases/delete-message.js';
import { markMessagesRead } from '../../../application/use-cases/mark-messages-read.js';
import { computeMeetingUniquenessKey } from '../../../domain/conversations/uniqueness-key.js';

export function registerMeetingHandlers(io: CommunicationIoServer, container: AppContainer): void {
  const emitter = new SocketRealtimeEmitter(io);
  io.on('connection', (socket) => {
    wireMeetingSocket(socket, io, container, emitter);
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

function wireMeetingSocket(
  socket: CommunicationSocket,
  io: CommunicationIoServer,
  container: AppContainer,
  emitter: SocketRealtimeEmitter,
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
    await container.sfu.closeParticipant(meetingId, leavingUserId).catch((err: unknown) =>
      logger.warn({ err, meetingId, userId: leavingUserId }, 'sfu closeParticipant failed'),
    );
  };

  socket.on(MeetingSocketEvents.Join, async (...args: unknown[]) => {
    const raw = args[0];
    const ack = typeof args[1] === 'function'
      ? (args[1] as (r: SocketAck<JoinMeetingResult>) => void)
      : undefined;
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
      const ticketMeetingId = typeof principal.raw['meeting_id'] === 'string' ? principal.raw['meeting_id'] : undefined;
      if (!ticketMeetingId || ticketMeetingId !== parsed.data.meetingId) {
        ack?.({ ok: false, code: 'Auth.Forbidden', message: 'Guest ticket is not scoped to this meeting.' });
        return;
      }
    } else {
      const joinPermCheck = await checkPermission(principal, CommunicationPermissions.MeetingJoin, container.userPermissions);
      if (!joinPermCheck.allowed) {
        ack?.({ ok: false, code: joinPermCheck.code, message: joinPermCheck.message });
        return;
      }
    }

    const guestDisplayName = typeof principal.raw['display_name'] === 'string' ? principal.raw['display_name'] : undefined;
    const guestInvitationId = typeof principal.raw['invitation_id'] === 'string' ? principal.raw['invitation_id'] : undefined;
    const selfDisplayName = isGuest ? (guestDisplayName ?? 'Invitado') : await resolveDisplayName(container.userDirectory, userId);
    const result = await joinMeeting(
      {
        tenantId,
        correlationId: socket.id,
        meetingId: parsed.data.meetingId,
        user: { userId, displayName: selfDisplayName, actorType: principal.actorType },
        ...(parsed.data.passcode !== undefined ? { passcode: parsed.data.passcode } : {}),
        ...(parsed.data.invitationToken !== undefined ? { invitationToken: parsed.data.invitationToken } : {}),
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
    ack?.({ ok: true, value: result.value });
    if (!result.value.requiresAdmission) {
      // Entra directo (sin sala de espera) -> ya esta Joined de verdad.
      await container.presence
        .markBusy({
          tenantId,
          userId,
          sourceId: parsed.data.meetingId,
          kind: 'Meeting',
          leaseSeconds: MEETING_BUSY_LEASE_SECONDS,
        })
        .catch((err: unknown) => logger.warn({ err }, 'presence markBusy (meeting join) failed'));
      const me = result.value.snapshot.participants.find((p) => p.userId === userId);
      if (me) {
        emitter.emitToMeeting({
          tenantId,
          meetingId: parsed.data.meetingId,
          event: MeetingSocketEvents.ParticipantChanged,
          envelope: envelope({ meetingId: parsed.data.meetingId, participant: me, sequence: 0 }),
        });
      }
    }
  });

  socket.on(MeetingSocketEvents.Leave, async (...args: unknown[]) => {
    const parsed = LeaveMeetingPayloadSchema.safeParse(args[0]);
    if (!parsed.success) return;
    const result = await leaveMeeting(
      { tenantId, correlationId: socket.id, meetingId: parsed.data.meetingId, userId },
      container,
    );
    if (!result.isSuccess) return;
    await socket.leave(`t:${tenantId}:m:${parsed.data.meetingId}`);
    if (result.value.conversationId) {
      await socket.leave(`t:${tenantId}:c:${result.value.conversationId}`);
    }
    await closeSfuForParticipant(parsed.data.meetingId, userId);
    const meetingAfterLeave = await container.meetings.findById(tenantId, parsed.data.meetingId);
    const snap = meetingAfterLeave?.toSnapshot();
    if (snap?.status === 'Ended') {
      await container.sfu.closeMeeting(parsed.data.meetingId).catch((err: unknown) =>
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
  });

  socket.on(MeetingSocketEvents.Admit, async (...args: unknown[]) => {
    const raw = args[0];
    const ack = typeof args[1] === 'function'
      ? (args[1] as (r: SocketAck<MeetingParticipantDto>) => void)
      : undefined;
    const parsed = AdmitPayloadSchema.safeParse(raw);
    if (!parsed.success) {
      ack?.({ ok: false, code: 'Meeting.BadPayload', message: parsed.error.message });
      return;
    }
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
      envelope: envelope({ meetingId: parsed.data.meetingId, participant: result.value.participant, sequence: 0 }),
    });
  });

  socket.on(MeetingSocketEvents.Remove, async (...args: unknown[]) => {
    const raw = args[0];
    const ack = typeof args[1] === 'function'
      ? (args[1] as (r: SocketAck<MeetingParticipantDto>) => void)
      : undefined;
    const parsed = RemovePayloadSchema.safeParse(raw);
    if (!parsed.success) {
      ack?.({ ok: false, code: 'Meeting.BadPayload', message: parsed.error.message });
      return;
    }
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
      envelope: envelope({ meetingId: parsed.data.meetingId, participant: result.value.participant, sequence: 0 }),
    });
  });

  socket.on(MeetingSocketEvents.Lock, async (...args: unknown[]) => {
    const parsed = LockPayloadSchema.safeParse(args[0]);
    if (!parsed.success) return;
    const result = await setMeetingLocked(
      { tenantId, correlationId: socket.id, meetingId: parsed.data.meetingId, hostUserId: userId, locked: parsed.data.locked },
      container,
    );
    if (!result.isSuccess) {
      logger.debug({ err: result.error }, 'Lock rejected');
      return;
    }
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
    const parsed = LeaveMeetingPayloadSchema.safeParse(args[0]);
    if (!parsed.success) return;
    const result = await muteAllInMeeting(
      { tenantId, meetingId: parsed.data.meetingId, hostUserId: userId },
      container,
    );
    if (!result.isSuccess) return;
    emitter.emitToMeeting({
      tenantId,
      meetingId: parsed.data.meetingId,
      event: MeetingSocketEvents.MutedByHost,
      envelope: envelope({ meetingId: parsed.data.meetingId, byUserId: userId }),
    });
  });

  socket.on(MeetingSocketEvents.TransferHost, async (...args: unknown[]) => {
    const parsed = TransferHostPayloadSchema.safeParse(args[0]);
    if (!parsed.success) return;
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
    if (!result.isSuccess) return;
    emitter.emitToMeeting({
      tenantId,
      meetingId: parsed.data.meetingId,
      event: MeetingSocketEvents.StateChanged,
      envelope: envelope({
        meetingId: parsed.data.meetingId,
        status: 'Live' as const,
        isLocked: false,
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
    const ack = typeof args[1] === 'function'
      ? (args[1] as (r: SocketAck<MeetingParticipantDto>) => void)
      : undefined;
    const parsed = PromoteCohostPayloadSchema.safeParse(raw);
    if (!parsed.success) {
      ack?.({ ok: false, code: 'Meeting.BadPayload', message: parsed.error.message });
      return;
    }
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
    const ack = typeof args[1] === 'function'
      ? (args[1] as (r: SocketAck<MeetingParticipantDto>) => void)
      : undefined;
    const parsed = DemoteCohostPayloadSchema.safeParse(raw);
    if (!parsed.success) {
      ack?.({ ok: false, code: 'Meeting.BadPayload', message: parsed.error.message });
      return;
    }
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
    const result = await updateMeetingMediaStatus(
      {
        tenantId,
        meetingId: parsed.data.meetingId,
        actorUserId: userId,
        audioEnabled: parsed.data.audioEnabled,
        videoEnabled: parsed.data.videoEnabled,
        screenSharing: parsed.data.screenSharing,
      },
      container,
    );
    if (!result.isSuccess) return;
    emitter.emitToMeeting({
      tenantId,
      meetingId: parsed.data.meetingId,
      event: MeetingSocketEvents.ParticipantChanged,
      envelope: envelope({ meetingId: parsed.data.meetingId, participant: result.value, sequence: 0 }),
    });
  });

  socket.on(MeetingSocketEvents.RaiseHand, async (...args: unknown[]) => {
    const parsed = MeetingRaiseHandPayloadSchema.safeParse(args[0]);
    if (!parsed.success) return;
    const result = await updateRaiseHand(
      { tenantId, meetingId: parsed.data.meetingId, actorUserId: userId, raised: parsed.data.raised },
      container,
    );
    if (!result.isSuccess) return;
    emitter.emitToMeeting({
      tenantId,
      meetingId: parsed.data.meetingId,
      event: MeetingSocketEvents.ParticipantChanged,
      envelope: envelope({ meetingId: parsed.data.meetingId, participant: result.value, sequence: 0 }),
    });
  });

  socket.on(MeetingSocketEvents.DominantSpeaker, async (...args: unknown[]) => {
    const parsed = DominantSpeakerPayloadSchema.safeParse(args[0]);
    if (!parsed.success) return;
    const allowed = await container.dominantSpeakerThrottle.allow({
      tenantId,
      meetingId: parsed.data.meetingId,
      userId,
    });
    if (!allowed) return;
    emitter.emitToMeeting({
      tenantId,
      meetingId: parsed.data.meetingId,
      event: MeetingSocketEvents.DominantSpeakerChanged,
      envelope: envelope({
        meetingId: parsed.data.meetingId,
        peerUserId: userId,
        audioLevel: parsed.data.audioLevel,
      }),
    });
  });

  socket.on(MeetingSocketEvents.AttachRecording, async (...args: unknown[]) => {
    const raw = args[0];
    const ack = typeof args[1] === 'function'
      ? (args[1] as (r: SocketAck<{ meetingId: string; recordingFileId: string }>) => void)
      : undefined;
    const parsed = AttachMeetingRecordingPayloadSchema.safeParse(raw);
    if (!parsed.success) {
      ack?.({ ok: false, code: 'Meeting.BadPayload', message: parsed.error.message });
      return;
    }
    const result = await attachMeetingRecording(
      {
        tenantId,
        correlationId: socket.id,
        clientKey: parsed.data.clientKey,
        meetingId: parsed.data.meetingId,
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
    const ack = typeof args[1] === 'function'
      ? (args[1] as (r: SocketAck<{ meetingId: string; participantUserIds: readonly string[]; requestedAtUtc: string }>) => void)
      : undefined;
    const parsed = RequestMeetingRecordingPayloadSchema.safeParse(raw);
    if (!parsed.success) {
      ack?.({ ok: false, code: 'Meeting.BadPayload', message: parsed.error.message });
      return;
    }
    const result = await requestMeetingRecording(
      {
        tenantId,
        correlationId: socket.id,
        clientKey: parsed.data.clientKey,
        meetingId: parsed.data.meetingId,
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
    const ack = typeof args[1] === 'function'
      ? (args[1] as (r: SocketAck<{ response: RecordingConsentEntryStatus }>) => void)
      : undefined;
    const parsed = RespondMeetingRecordingConsentPayloadSchema.safeParse(raw);
    if (!parsed.success) {
      ack?.({ ok: false, code: 'Meeting.BadPayload', message: parsed.error.message });
      return;
    }
    const result = await respondMeetingRecordingConsent(
      {
        tenantId,
        correlationId: socket.id,
        meetingId: parsed.data.meetingId,
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
    const ack = typeof args[1] === 'function'
      ? (args[1] as (r: SocketAck<{ meetingId: string; elapsedSeconds: number }>) => void)
      : undefined;
    const parsed = StopMeetingRecordingPayloadSchema.safeParse(raw);
    if (!parsed.success) {
      ack?.({ ok: false, code: 'Meeting.BadPayload', message: parsed.error.message });
      return;
    }
    const result = await stopMeetingRecording(
      {
        tenantId,
        correlationId: socket.id,
        clientKey: parsed.data.clientKey,
        meetingId: parsed.data.meetingId,
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
    const ack = typeof args[1] === 'function' ? (args[1] as (r: SocketAck<{ message: MessageDto }>) => void) : undefined;
    const parsed = MeetingChatSendPayloadSchema.safeParse(raw);
    if (!parsed.success) {
      ack?.({ ok: false, code: 'Meeting.BadPayload', message: parsed.error.message });
      return;
    }
    const conversationId = await resolveMeetingConversationId(parsed.data.meetingId);
    if (!conversationId) {
      ack?.({ ok: false, code: 'Meeting.Chat.NotStarted', message: 'Meeting chat has not been created yet.' });
      return;
    }
    const allowed = await container.rateLimiter.allow({
      scope: 'meeting.chat.send',
      tenantId,
      userId,
      maxPerWindow: config.rateLimit.meetingChatSend.maxPerWindow,
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
  });

  socket.on(MeetingSocketEvents.ChatEdit, async (...args: unknown[]) => {
    const raw = args[0];
    const ack = typeof args[1] === 'function' ? (args[1] as (r: SocketAck<{ edited: MessageEditedDto }>) => void) : undefined;
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
    const ack = typeof args[1] === 'function' ? (args[1] as (r: SocketAck<{ deleted: MessageDeletedDto }>) => void) : undefined;
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
    const ack = typeof args[1] === 'function' ? (args[1] as (r: SocketAck<{ markedCount: number }>) => void) : undefined;
    const parsed = MeetingChatMarkReadPayloadSchema.safeParse(raw);
    if (!parsed.success) {
      ack?.({ ok: false, code: 'Meeting.BadPayload', message: parsed.error.message });
      return;
    }
    const conversationId = await resolveMeetingConversationId(parsed.data.meetingId);
    if (!conversationId) {
      ack?.({ ok: false, code: 'Meeting.Chat.NotStarted', message: 'Meeting chat has not been created yet.' });
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
    };
    socket.to(`t:${tenantId}:m:${parsed.data.meetingId}`).emit(MeetingSocketEvents.SfuNewProducer, envelope(newProducer));
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

  socket.on(MeetingSocketEvents.SfuListRemoteProducers, async (...args: unknown[]) => {
    const ack = typeof args[1] === 'function' ? (args[1] as (r: SocketAck<unknown>) => void) : undefined;
    const parsed = SfuListRemoteProducersPayloadSchema.safeParse(args[0]);
    if (!parsed.success) {
      ack?.({ ok: false, code: 'Meeting.BadPayload', message: parsed.error.message });
      return;
    }
    const result = await listSfuRemoteProducers({ tenantId, meetingId: parsed.data.meetingId, userId }, container);
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
  socket.on('disconnect', () => {
    const meetingRoomPrefix = `t:${tenantId}:m:`;
    const activeMeetingIds = [...socket.rooms].filter((room) => room.startsWith(meetingRoomPrefix));
    for (const room of activeMeetingIds) {
      const meetingId = room.slice(meetingRoomPrefix.length);
      void leaveMeeting({ tenantId, correlationId: socket.id, meetingId, userId }, container)
        .then(async (result) => {
          if (!result.isSuccess) return;
          await closeSfuForParticipant(meetingId, userId);
          const meetingAfterLeave = await container.meetings.findById(tenantId, meetingId);
          const snap = meetingAfterLeave?.toSnapshot();
          if (snap?.status === 'Ended') {
            await container.sfu.closeMeeting(meetingId).catch((err: unknown) =>
              logger.warn({ err, meetingId }, 'sfu closeMeeting failed'),
            );
            await clearMeetingBusyFor(
              container,
              tenantId,
              meetingId,
              snap.participants.map((p) => p.userId),
            );
          } else {
            await clearMeetingBusyFor(container, tenantId, meetingId, [userId]);
          }
          emitter.emitToMeeting({
            tenantId,
            meetingId,
            event: MeetingSocketEvents.StateChanged,
            envelope: envelope({
              meetingId,
              status: snap?.status ?? 'Live',
              isLocked: snap?.isLocked ?? false,
              hostUserId: snap?.hostUserId ?? userId,
              sequence: 0,
            }),
          });
        })
        .catch((err: unknown) => logger.warn({ err, meetingId }, 'leave meeting on disconnect failed'));
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
