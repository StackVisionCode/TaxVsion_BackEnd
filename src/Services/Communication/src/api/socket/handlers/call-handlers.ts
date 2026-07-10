import { randomUUID } from 'node:crypto';
import { logger } from '../../../infrastructure/logger/logger.js';
import { hasPermission, CommunicationPermissions } from '../../../domain/shared/permissions.js';
import type { AppContainer } from '../../../infrastructure/container.js';
import type {
  CommunicationIoServer,
  CommunicationSocket,
} from '../../../infrastructure/socket/build-io.js';
import { SocketRealtimeEmitter } from '../../../infrastructure/socket/socket-realtime-emitter.js';
import { initiateCall } from '../../../application/use-cases/initiate-call.js';
import { respondToCall } from '../../../application/use-cases/respond-to-call.js';
import { endCall } from '../../../application/use-cases/end-call.js';
import { updateCallMediaStatus } from '../../../application/use-cases/update-call-media-status.js';
import { relayCallSignal } from '../../../application/use-cases/relay-call-signal.js';
import {
  CallActionPayloadSchema,
  CallSignalPayloadSchema,
  CallSocketEvents,
  ConnectionQualityPayloadSchema,
  InitiateCallPayloadSchema,
  MediaStatusPayloadSchema,
  type IncomingCallDto,
} from '../../../contracts/socket/call-socket-events.js';
import type { SocketAck, SocketEnvelope } from '../../../contracts/socket/socket-envelope.js';

/**
 * Handlers de calls 1:1. Cada handler:
 *   1. Zod para validar payload (nunca confiar en el cliente).
 *   2. Permiso lo lee SOLO del JWT verificado.
 *   3. Delegar al use case → ack con Result<T>.
 *   4. Emitir a los rooms tenant-scoped:
 *      - `t:{tenantId}:u:{calleeUserId}` para incoming call.
 *      - `t:{tenantId}:c:call:{callId}` para state/signal/media.
 */
export function registerCallHandlers(io: CommunicationIoServer, container: AppContainer): void {
  const emitter = new SocketRealtimeEmitter(io);
  io.on('connection', (socket) => {
    wireCallSocket(socket, container, emitter);
  });
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
    const result = await initiateCall(
      {
        tenantId,
        correlationId: socket.id,
        clientKey: parsed.data.clientKey,
        kind: parsed.data.kind,
        caller: { userId, displayName: principal.userId },
        callee: { userId: parsed.data.calleeUserId, displayName: parsed.data.calleeUserId },
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
    }
    ack?.({ ok: true, value: result.value });
    emitter.emitToConversation({
      tenantId,
      conversationId: `call:${parsed.data.callId}`,
      event: CallSocketEvents.StateChanged,
      envelope: envelope(result.value.state),
    });
    if (result.value.peer) {
      emitter.emitToConversation({
        tenantId,
        conversationId: `call:${parsed.data.callId}`,
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
    ack?.({ ok: true, value: result.value });
    emitter.emitToConversation({
      tenantId,
      conversationId: `call:${parsed.data.callId}`,
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
    emitter.emitToConversation({
      tenantId,
      conversationId: `call:${parsed.data.callId}`,
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
}

function envelope<T>(payload: T): SocketEnvelope<T> {
  return {
    eventId: randomUUID(),
    correlationId: '',
    emittedAtUtc: new Date().toISOString(),
    payload,
  };
}
