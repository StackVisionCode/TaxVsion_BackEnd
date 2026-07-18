import { randomUUID } from 'node:crypto';
import type { Redis } from 'ioredis';
import { logger } from '../logger/logger.js';
import type { RealtimeEmitter } from '../../application/ports/realtime-emitter.js';
import { NotificationSocketEvents, type SessionRevokedDto } from '../../contracts/socket/notification-socket-events.js';
import { config } from '../config.js';

/**
 * Suscribe al canal Pub/Sub que publica Auth cuando revoca una sesion. El
 * payload trae { tenantId, userId, sessionId, jti, reason }. Emitimos socket
 * `session.revoked` al room `t:{tenantId}:u:{userId}` — canal SEPARADO del de
 * notifications de negocio (cierre CRIT legacy).
 */
export interface SessionRevokedMessage {
  tenantId: string;
  userId: string;
  sessionId?: string | null;
  jti?: string | null;
  reason?: string;
  revokedAtUtc?: string;
}

const CHANNEL = 'auth:session-revoked';

export function startSessionDenylistWatcher(
  subscriber: Redis,
  emitter: RealtimeEmitter,
): { stop(): Promise<void> } {
  void subscriber.subscribe(CHANNEL).catch((err: unknown) => {
    logger.error({ err: (err as Error).message }, 'session denylist subscribe failed');
  });

  const handler = (channel: string, raw: string): void => {
    if (channel !== CHANNEL) return;
    let parsed: SessionRevokedMessage;
    try {
      parsed = JSON.parse(raw) as SessionRevokedMessage;
    } catch {
      logger.warn({ raw }, 'session revoked: unparseable payload');
      return;
    }
    if (!parsed.tenantId || !parsed.userId) return;

    const dto: SessionRevokedDto = {
      sessionId: parsed.sessionId ?? null,
      jti: parsed.jti ?? null,
      reason: parsed.reason ?? 'Unknown',
      revokedAtUtc: parsed.revokedAtUtc ?? new Date().toISOString(),
    };
    emitter.emitToUser({
      tenantId: parsed.tenantId,
      userId: parsed.userId,
      event: NotificationSocketEvents.SessionRevoked,
      envelope: {
        eventId: randomUUID(),
        correlationId: '',
        emittedAtUtc: new Date().toISOString(),
        payload: dto,
      },
    });
    // Denylist en Redis con TTL corto; el JWKS-verifier ya la consulta en cada
    // handshake nuevo. Solo confirmamos que existe la entrada.
    logger.info(
      { tenantId: parsed.tenantId, userId: parsed.userId, prefix: config.redis.sessionDenylistPrefix },
      'session revoked broadcast',
    );
  };

  subscriber.on('message', handler);
  return {
    async stop(): Promise<void> {
      subscriber.off('message', handler);
      await subscriber.unsubscribe(CHANNEL).catch(() => undefined);
    },
  };
}
