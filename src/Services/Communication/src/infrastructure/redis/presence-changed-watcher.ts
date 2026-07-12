import { randomUUID } from 'node:crypto';
import type { Redis } from 'ioredis';
import { logger } from '../logger/logger.js';
import type { RealtimeEmitter } from '../../application/ports/realtime-emitter.js';
import { ChatSocketEvents, type PresenceChangedDto } from '../../contracts/socket/chat-socket-events.js';

/**
 * Suscribe (via PSUBSCRIBE) al canal Pub/Sub que publica `RedisPresenceService`
 * cuando un usuario transita entre online/offline. El canal es
 * `comm:presence:changed:{tenantId}` — el `tenantId` se lee del propio nombre
 * del canal para no confiar en el payload.
 *
 * Emite `chat.presence.changed` al room `t:{tenantId}` para que cualquier
 * socket del tenant (que se preocupe por presencia) reciba el update.
 * Broadcast al tenant y no al room de conversation porque presence es global
 * al tenant, no ligada a una conversacion; el FE filtra localmente por
 * los userIds que le interesan (participantes de sus conversaciones).
 *
 * Cierra el gap donde `RedisPresenceService` publicaba pero nadie subscribia,
 * dejando al FE sin manera de saber quien esta online sin polling.
 */
export interface PresenceChangedMessage {
  userId: string;
  online: boolean;
  changedAtUtc: string;
}

const CHANNEL_PATTERN = 'comm:presence:changed:*';
const CHANNEL_PREFIX = 'comm:presence:changed:';

export function startPresenceChangedWatcher(
  subscriber: Redis,
  emitter: RealtimeEmitter,
): { stop(): Promise<void> } {
  void subscriber.psubscribe(CHANNEL_PATTERN).catch((err: unknown) => {
    logger.error({ err: (err as Error).message }, 'presence changed subscribe failed');
  });

  const handler = (_pattern: string, channel: string, raw: string): void => {
    if (!channel.startsWith(CHANNEL_PREFIX)) return;
    const tenantId = channel.slice(CHANNEL_PREFIX.length);
    if (!tenantId) return;

    let parsed: PresenceChangedMessage;
    try {
      parsed = JSON.parse(raw) as PresenceChangedMessage;
    } catch {
      logger.warn({ raw, channel }, 'presence changed: unparseable payload');
      return;
    }
    if (!parsed.userId || typeof parsed.online !== 'boolean') return;

    const dto: PresenceChangedDto = {
      userId: parsed.userId,
      online: parsed.online,
      changedAtUtc: parsed.changedAtUtc ?? new Date().toISOString(),
    };

    emitter.emitToTenant({
      tenantId,
      event: ChatSocketEvents.PresenceChanged,
      envelope: {
        eventId: randomUUID(),
        correlationId: '',
        emittedAtUtc: new Date().toISOString(),
        payload: dto,
      },
    });
  };

  subscriber.on('pmessage', handler);
  return {
    async stop(): Promise<void> {
      subscriber.off('pmessage', handler);
      await subscriber.punsubscribe(CHANNEL_PATTERN).catch(() => undefined);
    },
  };
}
