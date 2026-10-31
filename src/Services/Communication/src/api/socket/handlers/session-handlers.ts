import { randomUUID } from 'node:crypto';
import { logger } from '../../../infrastructure/logger/logger.js';
import type { AppContainer } from '../../../infrastructure/container.js';
import type { CommunicationIoServer, CommunicationSocket } from '../../../infrastructure/socket/build-io.js';
import { SocketRealtimeEmitter } from '../../../infrastructure/socket/socket-realtime-emitter.js';
import { UnauthorizedError, verifyAccessToken } from '../../../infrastructure/jwks/jwt-verifier.js';
import type { SessionRevokedMessage } from '../../../infrastructure/redis/session-denylist-watcher.js';
import {
  AuthRefreshPayloadSchema,
  SessionSocketEvents,
  type AuthRefreshResultDto,
  type SessionExpiringDto,
} from '../../../contracts/socket/session-socket-events.js';
import type { SocketAck } from '../../../contracts/socket/socket-envelope.js';
import type { ForcedLeaveReason } from '../../../contracts/events/meeting-events.js';
import { forceLeaveMeeting } from '../../../application/use-cases/force-leave-meeting.js';

/** Margen tras el `exp` antes de cortar (relojes desfasados) y antelación del aviso `session.expiring`. */
const EXPIRY_GRACE_MS = 5_000;
const EXPIRING_NOTICE_MS = 60_000;
/** setTimeout no admite más de ~24,8 días; un token tan largo se re-programa al vencer el timer. */
const MAX_TIMER_MS = 2_147_483_647;

/**
 * Sesión del socket (INV-09 §13.1–13.2, §13.6): cada socket se cierra al `exp` de su token (con
 * `auth.refresh` para renovar sin reconectar) y una sesión revocada por Auth expulsa al socket exacto
 * de sus reuniones y llamadas en vez de esperar la gracia de 30 s.
 */
export function registerSessionHandlers(
  io: CommunicationIoServer,
  container: AppContainer,
): { revoke(message: SessionRevokedMessage): Promise<void> } {
  const emitter = new SocketRealtimeEmitter(io);
  io.on('connection', (socket) => wireSessionSocket(socket, container, emitter));
  return {
    async revoke(message) {
      for (const socket of io.sockets.sockets.values()) {
        const principal = socket.data.principal;
        if (!principal || principal.tenantId !== message.tenantId || principal.userId !== message.userId) continue;
        // Sin `sid` en el mensaje se cierran todas las sesiones del usuario (baja/suspensión).
        if (message.sessionId && principal.sessionId !== message.sessionId) continue;
        await closeSocket(socket, container, emitter, 'Revoked');
      }
    },
  };
}

function wireSessionSocket(socket: CommunicationSocket, container: AppContainer, emitter: SocketRealtimeEmitter): void {
  let expiryTimer: ReturnType<typeof setTimeout> | undefined;
  let noticeTimer: ReturnType<typeof setTimeout> | undefined;

  const clearTimers = (): void => {
    if (expiryTimer) clearTimeout(expiryTimer);
    if (noticeTimer) clearTimeout(noticeTimer);
    expiryTimer = undefined;
    noticeTimer = undefined;
  };

  const schedule = (): void => {
    clearTimers();
    const expiresAtMs = tokenExpiryMs(socket);
    if (expiresAtMs === null) return;
    const untilClose = expiresAtMs + EXPIRY_GRACE_MS - Date.now();
    if (untilClose > MAX_TIMER_MS) {
      expiryTimer = setTimeout(schedule, MAX_TIMER_MS);
      return;
    }
    const untilNotice = untilClose - EXPIRING_NOTICE_MS;
    if (untilNotice > 0) {
      noticeTimer = setTimeout(() => {
        const payload: SessionExpiringDto = {
          expiresAtUtc: new Date(expiresAtMs).toISOString(),
          secondsLeft: Math.max(0, Math.round((expiresAtMs - Date.now()) / 1000)),
        };
        socket.emit(SessionSocketEvents.SessionExpiring, {
          eventId: randomUUID(),
          correlationId: '',
          emittedAtUtc: new Date().toISOString(),
          payload,
        });
      }, untilNotice);
    }
    expiryTimer = setTimeout(() => {
      void closeSocket(socket, container, emitter, 'Expired');
    }, Math.max(0, untilClose));
  };

  socket.on(SessionSocketEvents.AuthRefresh, async (...args: unknown[]) => {
    const ack = typeof args[1] === 'function' ? (args[1] as (r: SocketAck<AuthRefreshResultDto>) => void) : undefined;
    const parsed = AuthRefreshPayloadSchema.safeParse(args[0]);
    if (!parsed.success) {
      ack?.({ ok: false, code: 'Auth.BadPayload', message: parsed.error.message });
      return;
    }
    const current = socket.data.principal;
    if (!current || current.actorType === 'Guest') {
      ack?.({ ok: false, code: 'Auth.Forbidden', message: 'This session cannot be refreshed.' });
      return;
    }
    try {
      const next = await verifyAccessToken(parsed.data.token);
      // El socket conserva sus rooms: solo se admite el mismo usuario de la misma oficina.
      if (next.userId !== current.userId || next.tenantId !== current.tenantId) {
        ack?.({ ok: false, code: 'Auth.Forbidden', message: 'The new token belongs to another session.' });
        return;
      }
      socket.data.principal = next;
      schedule();
      const expiresAtMs = tokenExpiryMs(socket);
      ack?.({ ok: true, value: { expiresAtUtc: new Date(expiresAtMs ?? Date.now()).toISOString() } });
    } catch (err) {
      ack?.({ ok: false, code: err instanceof UnauthorizedError ? err.code : 'Auth.InvalidToken', message: 'Token rejected.' });
    }
  });

  socket.on('disconnect', clearTimers);
  schedule();
}

function tokenExpiryMs(socket: CommunicationSocket): number | null {
  const exp = socket.data.principal?.raw['exp'];
  return typeof exp === 'number' && Number.isFinite(exp) ? exp * 1000 : null;
}

/**
 * Salida inmediata del socket: cada reunión en sus rooms pasa por la salida forzada (T-08) y la
 * llamada activa, si la hay, la cierra el handler de `disconnect` de calls al desconectarlo.
 */
async function closeSocket(
  socket: CommunicationSocket,
  container: AppContainer,
  emitter: SocketRealtimeEmitter,
  reason: Extract<ForcedLeaveReason, 'Revoked' | 'Expired'>,
): Promise<void> {
  const principal = socket.data.principal;
  if (!principal) {
    socket.disconnect(true);
    return;
  }
  const prefix = `t:${principal.tenantId}:m:`;
  const meetingRooms = [...socket.rooms].filter((room) => room.startsWith(prefix));
  for (const room of meetingRooms) {
    const meetingId = room.slice(prefix.length);
    const result = await forceLeaveMeeting(
      {
        tenantId: principal.tenantId,
        meetingId,
        userId: principal.userId,
        reason,
        correlationId: `${reason.toLowerCase()}:${socket.id}`,
        socketId: socket.id,
      },
      { ...container, emitter },
    ).catch((err: unknown) => {
      logger.warn({ err, meetingId, reason }, 'forced leave on session close failed');
      return null;
    });
    // Fuera de la room antes del `disconnect`: así el handler de desconexión no agenda otra salida.
    await socket.leave(room);
    if (result?.isSuccess && result.value.conversationId) {
      await socket.leave(`t:${principal.tenantId}:c:${result.value.conversationId}`);
    }
  }
  logger.info({ sid: socket.id, userId: principal.userId, reason, meetings: meetingRooms.length }, 'socket closed by session rule');
  socket.disconnect(true);
}
