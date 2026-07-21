import { randomUUID } from 'node:crypto';
import { config } from '../../../infrastructure/config.js';
import { logger } from '../../../infrastructure/logger/logger.js';
import { hasPermission, CommunicationPermissions } from '../../../domain/shared/permissions.js';
import type { AppContainer } from '../../../infrastructure/container.js';
import type {
  CommunicationIoServer,
  CommunicationSocket,
} from '../../../infrastructure/socket/build-io.js';
import { SocketRealtimeEmitter } from '../../../infrastructure/socket/socket-realtime-emitter.js';
import { resolveDisplayName } from './resolve-display-name.js';
import { initiateCall } from '../../../application/use-cases/initiate-call.js';
import { respondToCall } from '../../../application/use-cases/respond-to-call.js';
import { endCall } from '../../../application/use-cases/end-call.js';
import { updateCallMediaStatus } from '../../../application/use-cases/update-call-media-status.js';
import { relayCallSignal } from '../../../application/use-cases/relay-call-signal.js';
import { attachCallRecording } from '../../../application/use-cases/attach-call-recording.js';
import { requestCallRecording } from '../../../application/use-cases/request-call-recording.js';
import { respondCallRecordingConsent } from '../../../application/use-cases/respond-call-recording-consent.js';
import { stopCallRecording } from '../../../application/use-cases/stop-call-recording.js';
import { upgradeCallToVideo } from '../../../application/use-cases/upgrade-call-to-video.js';
import { startCallScreenShare } from '../../../application/use-cases/start-call-screen-share.js';
import { stopCallScreenShare } from '../../../application/use-cases/stop-call-screen-share.js';
import {
  AttachCallRecordingPayloadSchema,
  CallActionPayloadSchema,
  CallSignalPayloadSchema,
  CallSocketEvents,
  ConnectionQualityPayloadSchema,
  InitiateCallPayloadSchema,
  MediaStatusPayloadSchema,
  RequestCallRecordingPayloadSchema,
  RespondCallRecordingConsentPayloadSchema,
  StartCallScreenSharePayloadSchema,
  StopCallRecordingPayloadSchema,
  StopCallScreenSharePayloadSchema,
  UpgradeCallToVideoPayloadSchema,
  type IncomingCallDto,
} from '../../../contracts/socket/call-socket-events.js';
import type { SocketAck, SocketEnvelope } from '../../../contracts/socket/socket-envelope.js';
import type { RecordingConsentEntryStatus } from '../../../domain/recording/recording-consent.js';

/**
 * Handlers de calls 1:1. Cada handler:
 *   1. Zod para validar payload (nunca confiar en el cliente).
 *   2. Permiso lo lee SOLO del JWT verificado.
 *   3. Delegar al use case → ack con Result<T>.
 *   4. Emitir a los rooms tenant-scoped:
 *      - `t:{tenantId}:u:{calleeUserId}` para incoming call.
 *      - `t:{tenantId}:call:{callId}` para state/peer/media (via `emitToCall`).
 *      - `t:{tenantId}:u:{targetUserId}` para signaling (via `emitToUser`).
 */
export function registerCallHandlers(io: CommunicationIoServer, container: AppContainer): void {
  const emitter = new SocketRealtimeEmitter(io);
  io.on('connection', (socket) => {
    wireCallSocket(socket, container, emitter);
  });
}

/**
 * Fase A3 — TTL de respaldo para el lease de "busy" de una call. En operacion
 * normal siempre se limpia explicitamente (accept/reject/cancel/end/disconnect);
 * este valor solo protege contra el caso en que ninguno de esos handlers
 * llegue a correr (crash del proceso a mitad de un `markBusy`).
 */
const CALL_BUSY_LEASE_SECONDS = 6 * 60 * 60;

/**
 * Limpia la fuente de "busy" de esta call para caller y callee. Se llama en
 * cualquier transicion terminal (Reject/Cancel/End) y en el fallback de
 * disconnect. clearBusy es un no-op seguro para quien nunca estuvo marcado
 * busy (ej. el callee en un Reject, que nunca llego a Accept) — countBusySources
 * da 0 y solo re-publica el status actual, sin efecto visible.
 */
async function clearCallBusyForBothParties(
  container: AppContainer,
  tenantId: string,
  callId: string,
): Promise<void> {
  const call = await container.calls.findById(tenantId, callId);
  if (!call) return;
  const snap = call.toSnapshot();
  await Promise.all(
    [snap.callerUserId, snap.calleeUserId].map((participantUserId) =>
      container.presence
        .clearBusy({ tenantId, userId: participantUserId, sourceId: callId })
        .catch((err: unknown) => logger.warn({ err, callId }, 'presence clearBusy (call) failed')),
    ),
  );
}

function wireCallSocket(
  socket: CommunicationSocket,
  container: AppContainer,
  emitter: SocketRealtimeEmitter,
): void {
  const principal = socket.data.principal;
  if (!principal) return;
  const { tenantId, userId } = principal;

  socket.on(CallSocketEvents.Initiate, async (...args: unknown[]) => {
    const raw = args[0];
    const ack = typeof args[1] === 'function'
      ? (args[1] as (r: SocketAck<{ callId: string; ringingAtUtc: string }>) => void)
      : undefined;
    const parsed = InitiateCallPayloadSchema.safeParse(raw);
    if (!parsed.success) {
      ack?.({ ok: false, code: 'Call.BadPayload', message: parsed.error.message });
      return;
    }
    const requiredPermission =
      parsed.data.kind === 'Video'
        ? CommunicationPermissions.VideoCallStart
        : CommunicationPermissions.CallStart;
    if (!hasPermission(principal.actorType, principal.permissions, requiredPermission)) {
      ack?.({ ok: false, code: 'Auth.Forbidden', message: `Missing ${requiredPermission}.` });
      return;
    }
    const allowed = await container.rateLimiter.allow({
      scope: 'call.initiate',
      tenantId,
      userId,
      maxPerWindow: config.rateLimit.callInitiate.maxPerWindow,
      windowSeconds: config.rateLimit.callInitiate.windowSeconds,
    });
    if (!allowed) {
      ack?.({ ok: false, code: 'Call.RateLimited', message: 'Too many call attempts, slow down.' });
      return;
    }
    const [callerDisplayName, calleeDisplayName] = await Promise.all([
      resolveDisplayName(container.userDirectory, userId),
      resolveDisplayName(container.userDirectory, parsed.data.calleeUserId),
    ]);
    const result = await initiateCall(
      {
        tenantId,
        correlationId: socket.id,
        clientKey: parsed.data.clientKey,
        kind: parsed.data.kind,
        caller: { userId, displayName: callerDisplayName },
        callee: { userId: parsed.data.calleeUserId, displayName: calleeDisplayName },
        conversationId: parsed.data.conversationId ?? null,
        recordingRequested: parsed.data.recordingRequested ?? false,
      },
      container,
    );
    if (!result.isSuccess) {
      ack?.({ ok: false, code: result.error.code, message: result.error.message });
      return;
    }
    await socket.join(`t:${tenantId}:call:${result.value.callId}`);
    // Fase A3 — el caller ya cuenta como "busy" desde que empieza a sonar
    // (dialing out): no puede razonablemente aceptar/iniciar otra llamada a
    // la vez. El callee recien se marca busy si/cuando acepta (mas abajo).
    await container.presence
      .markBusy({
        tenantId,
        userId,
        sourceId: result.value.callId,
        kind: 'Call',
        leaseSeconds: CALL_BUSY_LEASE_SECONDS,
      })
      .catch((err: unknown) => logger.warn({ err }, 'presence markBusy (call initiate) failed'));
    ack?.({ ok: true, value: result.value });
    const incoming: IncomingCallDto = {
      callId: result.value.callId,
      callerUserId: userId,
      callerDisplayName: principal.userId,
      calleeUserId: parsed.data.calleeUserId,
      kind: parsed.data.kind,
      conversationId: parsed.data.conversationId ?? null,
      ringingAtUtc: result.value.ringingAtUtc,
    };
    emitter.emitToUser({
      tenantId,
      userId: parsed.data.calleeUserId,
      event: CallSocketEvents.Incoming,
      envelope: envelope(incoming),
    });
  });

  const respondAction = async (
    action: 'accept' | 'reject' | 'cancel',
    args: unknown[],
  ): Promise<void> => {
    const raw = args[0];
    const ack = typeof args[1] === 'function'
      ? (args[1] as (r: SocketAck<unknown>) => void)
      : undefined;
    const parsed = CallActionPayloadSchema.safeParse(raw);
    if (!parsed.success) {
      ack?.({ ok: false, code: 'Call.BadPayload', message: parsed.error.message });
      return;
    }
    const result = await respondToCall(
      {
        tenantId,
        correlationId: socket.id,
        clientKey: parsed.data.clientKey,
        callId: parsed.data.callId,
        actorUserId: userId,
        actorDisplayName: principal.userId,
        action,
      },
      container,
    );
    if (!result.isSuccess) {
      ack?.({ ok: false, code: result.error.code, message: result.error.message });
      return;
    }
    if (action === 'accept') {
      await socket.join(`t:${tenantId}:call:${parsed.data.callId}`);
      // Fase A3 — el callee recien se marca busy al aceptar (el caller ya lo
      // esta desde Initiate).
      await container.presence
        .markBusy({
          tenantId,
          userId,
          sourceId: parsed.data.callId,
          kind: 'Call',
          leaseSeconds: CALL_BUSY_LEASE_SECONDS,
        })
        .catch((err: unknown) => logger.warn({ err }, 'presence markBusy (call accept) failed'));
    } else {
      // reject/cancel: la call termina desde Ringing — limpia cualquier
      // fuente busy que haya quedado (en la practica, solo el caller).
      await clearCallBusyForBothParties(container, tenantId, parsed.data.callId);
    }
    ack?.({ ok: true, value: result.value });
    emitter.emitToCall({
      tenantId,
      callId: parsed.data.callId,
      event: CallSocketEvents.StateChanged,
      envelope: envelope(result.value.state),
    });
    if (result.value.peer) {
      emitter.emitToCall({
        tenantId,
        callId: parsed.data.callId,
        event: CallSocketEvents.PeerJoined,
        envelope: envelope(result.value.peer),
      });
    }
  };

  socket.on(CallSocketEvents.Accept, (...args: unknown[]) => void respondAction('accept', args));
  socket.on(CallSocketEvents.Reject, (...args: unknown[]) => void respondAction('reject', args));
  socket.on(CallSocketEvents.Cancel, (...args: unknown[]) => void respondAction('cancel', args));

  socket.on(CallSocketEvents.End, async (...args: unknown[]) => {
    const raw = args[0];
    const ack = typeof args[1] === 'function'
      ? (args[1] as (r: SocketAck<unknown>) => void)
      : undefined;
    const parsed = CallActionPayloadSchema.safeParse(raw);
    if (!parsed.success) {
      ack?.({ ok: false, code: 'Call.BadPayload', message: parsed.error.message });
      return;
    }
    const result = await endCall(
      {
        tenantId,
        correlationId: socket.id,
        clientKey: parsed.data.clientKey,
        callId: parsed.data.callId,
        actorUserId: userId,
      },
      container,
    );
    if (!result.isSuccess) {
      ack?.({ ok: false, code: result.error.code, message: result.error.message });
      return;
    }
    await clearCallBusyForBothParties(container, tenantId, parsed.data.callId);
    ack?.({ ok: true, value: result.value });
    emitter.emitToCall({
      tenantId,
      callId: parsed.data.callId,
      event: CallSocketEvents.StateChanged,
      envelope: envelope(result.value.state),
    });
  });

  socket.on(CallSocketEvents.Signal, async (...args: unknown[]) => {
    const raw = args[0];
    const ack = typeof args[1] === 'function'
      ? (args[1] as (r: SocketAck<{ delivered: true }>) => void)
      : undefined;
    const parsed = CallSignalPayloadSchema.safeParse(raw);
    if (!parsed.success) {
      ack?.({ ok: false, code: 'Call.BadPayload', message: parsed.error.message });
      return;
    }
    const allowed = await container.rateLimiter.allow({
      scope: 'call.signal',
      tenantId,
      userId,
      maxPerWindow: config.rateLimit.callSignal.maxPerWindow,
      windowSeconds: config.rateLimit.callSignal.windowSeconds,
    });
    if (!allowed) {
      ack?.({ ok: false, code: 'Call.RateLimited', message: 'Too many signaling messages, slow down.' });
      return;
    }
    const result = await relayCallSignal(
      {
        tenantId,
        callId: parsed.data.callId,
        fromUserId: userId,
        targetPeerUserId: parsed.data.targetPeerUserId,
        kind: parsed.data.kind,
        data: parsed.data.data,
      },
      container,
    );
    if (!result.isSuccess) {
      ack?.({ ok: false, code: result.error.code, message: result.error.message });
      return;
    }
    emitter.emitToUser({
      tenantId,
      userId: result.value.targetUserId,
      event: CallSocketEvents.SignalFrom,
      envelope: envelope(result.value.signal),
    });
    ack?.({ ok: true, value: { delivered: true } });
  });

  socket.on(CallSocketEvents.MediaStatus, async (...args: unknown[]) => {
    const parsed = MediaStatusPayloadSchema.safeParse(args[0]);
    if (!parsed.success) return;
    const result = await updateCallMediaStatus(
      {
        tenantId,
        callId: parsed.data.callId,
        actorUserId: userId,
        audioEnabled: parsed.data.audioEnabled,
        videoEnabled: parsed.data.videoEnabled,
        screenSharing: parsed.data.screenSharing,
      },
      container,
    );
    if (!result.isSuccess) {
      logger.debug({ err: result.error }, 'MediaStatus update rejected');
      return;
    }
    emitter.emitToCall({
      tenantId,
      callId: parsed.data.callId,
      event: CallSocketEvents.MediaStatusChanged,
      envelope: envelope(result.value.status),
    });
  });

  socket.on(CallSocketEvents.ConnectionQuality, async (...args: unknown[]) => {
    const parsed = ConnectionQualityPayloadSchema.safeParse(args[0]);
    if (!parsed.success) return;
    const call = await container.calls.findById(tenantId, parsed.data.callId);
    if (!call) return;
    call.reportConnectionQuality({ byUserId: userId, quality: parsed.data.quality });
    await container.calls.save(call);
  });

  socket.on(CallSocketEvents.AttachRecording, async (...args: unknown[]) => {
    const raw = args[0];
    const ack = typeof args[1] === 'function'
      ? (args[1] as (r: SocketAck<{ callId: string; recordingFileId: string }>) => void)
      : undefined;
    const parsed = AttachCallRecordingPayloadSchema.safeParse(raw);
    if (!parsed.success) {
      ack?.({ ok: false, code: 'Call.BadPayload', message: parsed.error.message });
      return;
    }
    const result = await attachCallRecording(
      {
        tenantId,
        correlationId: socket.id,
        clientKey: parsed.data.clientKey,
        callId: parsed.data.callId,
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
  // Recording consent (Fase Backend 4) — mismo criterio que meetings: sin
  // handler socket separado para "start" (nunca lo dispara el cliente
  // directo, solo respond-call-recording-consent.ts via auto-invoke o el
  // timeout scheduler a los 15s). Policy fija AllAcceptedRequired.
  // ---------------------------------------------------------------------

  socket.on(CallSocketEvents.RequestRecording, async (...args: unknown[]) => {
    const raw = args[0];
    const ack = typeof args[1] === 'function'
      ? (args[1] as (r: SocketAck<{ callId: string; participantUserIds: readonly string[]; requestedAtUtc: string }>) => void)
      : undefined;
    const parsed = RequestCallRecordingPayloadSchema.safeParse(raw);
    if (!parsed.success) {
      ack?.({ ok: false, code: 'Call.BadPayload', message: parsed.error.message });
      return;
    }
    const result = await requestCallRecording(
      {
        tenantId,
        correlationId: socket.id,
        clientKey: parsed.data.clientKey,
        callId: parsed.data.callId,
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

  socket.on(CallSocketEvents.RespondRecordingConsent, async (...args: unknown[]) => {
    const raw = args[0];
    const ack = typeof args[1] === 'function'
      ? (args[1] as (r: SocketAck<{ response: RecordingConsentEntryStatus }>) => void)
      : undefined;
    const parsed = RespondCallRecordingConsentPayloadSchema.safeParse(raw);
    if (!parsed.success) {
      ack?.({ ok: false, code: 'Call.BadPayload', message: parsed.error.message });
      return;
    }
    const result = await respondCallRecordingConsent(
      {
        tenantId,
        correlationId: socket.id,
        callId: parsed.data.callId,
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

  socket.on(CallSocketEvents.StopRecording, async (...args: unknown[]) => {
    const raw = args[0];
    const ack = typeof args[1] === 'function'
      ? (args[1] as (r: SocketAck<{ callId: string; elapsedSeconds: number }>) => void)
      : undefined;
    const parsed = StopCallRecordingPayloadSchema.safeParse(raw);
    if (!parsed.success) {
      ack?.({ ok: false, code: 'Call.BadPayload', message: parsed.error.message });
      return;
    }
    const result = await stopCallRecording(
      {
        tenantId,
        correlationId: socket.id,
        clientKey: parsed.data.clientKey,
        callId: parsed.data.callId,
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

  socket.on(CallSocketEvents.UpgradeToVideo, async (...args: unknown[]) => {
    const raw = args[0];
    const ack = typeof args[1] === 'function'
      ? (args[1] as (r: SocketAck<{ callId: string; upgradedAtUtc: string }>) => void)
      : undefined;
    const parsed = UpgradeCallToVideoPayloadSchema.safeParse(raw);
    if (!parsed.success) {
      ack?.({ ok: false, code: 'Call.BadPayload', message: parsed.error.message });
      return;
    }
    const result = await upgradeCallToVideo(
      {
        tenantId,
        correlationId: socket.id,
        clientKey: parsed.data.clientKey,
        callId: parsed.data.callId,
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

  socket.on(CallSocketEvents.ScreenShareStart, async (...args: unknown[]) => {
    const raw = args[0];
    const ack = typeof args[1] === 'function'
      ? (args[1] as (r: SocketAck<{ callId: string; startedAtUtc: string }>) => void)
      : undefined;
    const parsed = StartCallScreenSharePayloadSchema.safeParse(raw);
    if (!parsed.success) {
      ack?.({ ok: false, code: 'Call.BadPayload', message: parsed.error.message });
      return;
    }
    const result = await startCallScreenShare(
      {
        tenantId,
        correlationId: socket.id,
        clientKey: parsed.data.clientKey,
        callId: parsed.data.callId,
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

  socket.on(CallSocketEvents.ScreenShareStop, async (...args: unknown[]) => {
    const raw = args[0];
    const ack = typeof args[1] === 'function'
      ? (
          args[1] as (
            r: SocketAck<{ callId: string; startedAtUtc: string; stoppedAtUtc: string; durationSeconds: number }>,
          ) => void
        )
      : undefined;
    const parsed = StopCallScreenSharePayloadSchema.safeParse(raw);
    if (!parsed.success) {
      ack?.({ ok: false, code: 'Call.BadPayload', message: parsed.error.message });
      return;
    }
    const result = await stopCallScreenShare(
      {
        tenantId,
        correlationId: socket.id,
        clientKey: parsed.data.clientKey,
        callId: parsed.data.callId,
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

  // Desconexion abrupta (crash, tab cerrada, red caida) a mitad de llamada: si
  // el socket seguia en algun room `t:{tenant}:call:{id}` (se joinea en
  // Initiate/Accept), terminamos esa call y avisamos al peer — sin esto, la
  // otra punta se queda "en llamada" indefinidamente con nadie del otro lado.
  socket.on('disconnect', () => {
    const callRoomPrefix = `t:${tenantId}:call:`;
    const activeCallIds = [...socket.rooms].filter((room) => room.startsWith(callRoomPrefix));
    for (const room of activeCallIds) {
      const callId = room.slice(callRoomPrefix.length);
      void endCall(
        {
          tenantId,
          correlationId: socket.id,
          clientKey: `disconnect:${socket.id}:${callId}`,
          callId,
          actorUserId: userId,
        },
        container,
      ).then(async (result) => {
        if (!result.isSuccess) return;
        await clearCallBusyForBothParties(container, tenantId, callId);
        emitter.emitToCall({
          tenantId,
          callId,
          event: CallSocketEvents.StateChanged,
          envelope: envelope(result.value.state),
        });
      }).catch((err: unknown) => logger.warn({ err, callId }, 'end call on disconnect failed'));
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
