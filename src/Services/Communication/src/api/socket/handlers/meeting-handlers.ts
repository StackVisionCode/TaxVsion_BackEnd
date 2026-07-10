import { randomUUID } from 'node:crypto';
import { logger } from '../../../infrastructure/logger/logger.js';
import { hasPermission, CommunicationPermissions } from '../../../domain/shared/permissions.js';
import type { AppContainer } from '../../../infrastructure/container.js';
import type {
  CommunicationIoServer,
  CommunicationSocket,
} from '../../../infrastructure/socket/build-io.js';
import { SocketRealtimeEmitter } from '../../../infrastructure/socket/socket-realtime-emitter.js';
import {
  AdmitPayloadSchema,
  DominantSpeakerPayloadSchema,
  JoinMeetingPayloadSchema,
  LeaveMeetingPayloadSchema,
  LockPayloadSchema,
  MeetingMediaStatusPayloadSchema,
  MeetingRaiseHandPayloadSchema,
  MeetingSignalPayloadSchema,
  MeetingSocketEvents,
  RemovePayloadSchema,
  TransferHostPayloadSchema,
  type MeetingParticipantDto,
  type MeetingSnapshotDto,
} from '../../../contracts/socket/meeting-socket-events.js';
import type { SocketAck, SocketEnvelope } from '../../../contracts/socket/socket-envelope.js';
import { joinMeeting } from '../../../application/use-cases/join-meeting.js';
import { relayMeetingSignal } from '../../../application/use-cases/relay-meeting-signal.js';
import { updateMeetingMediaStatus, updateRaiseHand } from '../../../application/use-cases/update-meeting-media-status.js';
import {
  admitParticipant,
  muteAllInMeeting,
  removeParticipant,
  setMeetingLocked,
  transferMeetingHost,
} from '../../../application/use-cases/meeting-host-actions.js';
import { endMeeting } from '../../../application/use-cases/meeting-lifecycle.js';

export function registerMeetingHandlers(io: CommunicationIoServer, container: AppContainer): void {
  const emitter = new SocketRealtimeEmitter(io);
  io.on('connection', (socket) => {
    wireMeetingSocket(socket, container, emitter);
  });
}

function wireMeetingSocket(
  socket: CommunicationSocket,
  container: AppContainer,
  emitter: SocketRealtimeEmitter,
): void {
  const principal = socket.data.principal;
  if (!principal) return;
  const { tenantId, userId } = principal;

  socket.on(MeetingSocketEvents.Join, async (...args: unknown[]) => {
    const raw = args[0];
    const ack = typeof args[1] === 'function'
      ? (args[1] as (r: SocketAck<{ snapshot: MeetingSnapshotDto; requiresAdmission: boolean }>) => void)
      : undefined;
    const parsed = JoinMeetingPayloadSchema.safeParse(raw);
    if (!parsed.success) {
      ack?.({ ok: false, code: 'Meeting.BadPayload', message: parsed.error.message });
      return;
    }
    if (!hasPermission(principal.actorType, principal.permissions, CommunicationPermissions.MeetingJoin)) {
      ack?.({ ok: false, code: 'Auth.Forbidden', message: 'Missing communication.meeting.join.' });
      return;
    }
    const result = await joinMeeting(
      {
        tenantId,
        meetingId: parsed.data.meetingId,
        user: { userId, displayName: principal.userId },
        ...(parsed.data.passcode !== undefined ? { passcode: parsed.data.passcode } : {}),
        ...(parsed.data.invitationToken !== undefined ? { invitationToken: parsed.data.invitationToken } : {}),
      },
      container,
    );
    if (!result.isSuccess) {
      ack?.({ ok: false, code: result.error.code, message: result.error.message });
      return;
    }
    await socket.join(`t:${tenantId}:m:${parsed.data.meetingId}`);
    ack?.({ ok: true, value: result.value });
    if (!result.value.requiresAdmission) {
      const me = result.value.snapshot.participants.find((p) => p.userId === userId);
      if (me) {
        emitter.emitToConversation({
          tenantId,
          conversationId: `m:${parsed.data.meetingId}`,
          event: MeetingSocketEvents.ParticipantChanged,
          envelope: envelope({ meetingId: parsed.data.meetingId, participant: me, sequence: 0 }),
        });
      }
    }
  });

  socket.on(MeetingSocketEvents.Leave, async (...args: unknown[]) => {
    const parsed = LeaveMeetingPayloadSchema.safeParse(args[0]);
    if (!parsed.success) return;
    const meeting = await container.meetings.findById(tenantId, parsed.data.meetingId);
    if (!meeting) return;
    meeting.leave({ userId });
    await container.meetings.save(meeting);
    await socket.leave(`t:${tenantId}:m:${parsed.data.meetingId}`);
    emitter.emitToConversation({
      tenantId,
      conversationId: `m:${parsed.data.meetingId}`,
      event: MeetingSocketEvents.StateChanged,
      envelope: envelope({
        meetingId: parsed.data.meetingId,
        status: meeting.status,
        isLocked: false,
        hostUserId: meeting.hostUserId,
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
    ack?.({ ok: true, value: result.value });
    emitter.emitToConversation({
      tenantId,
      conversationId: `m:${parsed.data.meetingId}`,
      event: MeetingSocketEvents.ParticipantChanged,
      envelope: envelope({ meetingId: parsed.data.meetingId, participant: result.value, sequence: 0 }),
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
    ack?.({ ok: true, value: result.value });
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
    emitter.emitToConversation({
      tenantId,
      conversationId: `m:${parsed.data.meetingId}`,
      event: MeetingSocketEvents.ParticipantChanged,
      envelope: envelope({ meetingId: parsed.data.meetingId, participant: result.value, sequence: 0 }),
    });
  });

  socket.on(MeetingSocketEvents.Lock, async (...args: unknown[]) => {
    const parsed = LockPayloadSchema.safeParse(args[0]);
    if (!parsed.success) return;
    const result = await setMeetingLocked(
      { tenantId, meetingId: parsed.data.meetingId, hostUserId: userId, locked: parsed.data.locked },
      container,
    );
    if (!result.isSuccess) {
      logger.debug({ err: result.error }, 'Lock rejected');
      return;
    }
    emitter.emitToConversation({
      tenantId,
      conversationId: `m:${parsed.data.meetingId}`,
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
    emitter.emitToConversation({
      tenantId,
      conversationId: `m:${parsed.data.meetingId}`,
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
        meetingId: parsed.data.meetingId,
        currentHostUserId: userId,
        newHostUserId: parsed.data.newHostUserId,
      },
      container,
    );
    if (!result.isSuccess) return;
    emitter.emitToConversation({
      tenantId,
      conversationId: `m:${parsed.data.meetingId}`,
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
    emitter.emitToConversation({
      tenantId,
      conversationId: `m:${parsed.data.meetingId}`,
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
    emitter.emitToConversation({
      tenantId,
      conversationId: `m:${parsed.data.meetingId}`,
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
    emitter.emitToConversation({
      tenantId,
      conversationId: `m:${parsed.data.meetingId}`,
      event: MeetingSocketEvents.DominantSpeakerChanged,
      envelope: envelope({
        meetingId: parsed.data.meetingId,
        peerUserId: userId,
        audioLevel: parsed.data.audioLevel,
      }),
    });
  });

  // Al desconectar, marcamos leave si estaba joined a algun meeting activo.
  // No hacemos scan; el cliente vuelve a hacer join si vuelve. En Fase 4 el
  // consumer de `session.revoked` limpiara sessions muertas explicitamente.
  socket.on('disconnect', () => {
    // No-op — leave manual mediante Meeting.Leave. Un scheduler futuro puede
    // detectar participants Joined sin heartbeat prolongado (fuera de scope Fase 3).
    void endMeeting;
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
