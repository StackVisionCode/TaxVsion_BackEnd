import { randomUUID } from 'node:crypto';
import type { Redis } from 'ioredis';
import { logger } from '../logger/logger.js';
import type { RealtimeEmitter } from '../../application/ports/realtime-emitter.js';
import type { PresenceAudienceRepository } from '../../application/ports/presence-audience-repository.js';
import { ChatSocketEvents, type PresenceChangedDto } from '../../contracts/socket/chat-socket-events.js';
import { isBusyReason, isPresenceStatus } from '../../domain/presence/presence-status.js';

/**
 * Suscribe (via PSUBSCRIBE) al canal Pub/Sub que publica `RedisPresenceService`
 * cuando un usuario transita de status (Online/Busy/Offline). El canal es
 * `comm:presence:changed:{tenantId}` — el `tenantId` se lee del propio nombre
 * del canal para no confiar en el payload.
 *
 * Emite al personal del tenant y a clientes asignados al preparador.
 */
export interface PresenceChangedMessage {
  userId: string;
  status: 'Online' | 'Busy' | 'Offline';
  busyReason: 'Call' | 'Meeting' | null;
  changedAtUtc: string;
}

const CHANNEL_PATTERN = 'comm:presence:changed:*';
const CHANNEL_PREFIX = 'comm:presence:changed:';

export function startPresenceChangedWatcher(
  subscriber: Redis,
  emitter: RealtimeEmitter,
  audiences: PresenceAudienceRepository,
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
    if (!parsed.userId || !isPresenceStatus(parsed.status)) return;

    void dispatchPresenceChanged(tenantId, parsed, emitter, audiences).catch((error: unknown) => {
      logger.error({ err: (error as Error).message, tenantId }, 'presence changed dispatch failed');
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

export async function dispatchPresenceChanged(
  tenantId: string,
  parsed: PresenceChangedMessage,
  emitter: RealtimeEmitter,
  audiences: PresenceAudienceRepository,
): Promise<void> {
  const dto: PresenceChangedDto = {
    userId: parsed.userId,
    status: parsed.status,
    busyReason: parsed.status === 'Busy' && isBusyReason(parsed.busyReason ?? '') ? parsed.busyReason : null,
    changedAtUtc: parsed.changedAtUtc ?? new Date().toISOString(),
  };
  const envelope = {
    eventId: randomUUID(),
    correlationId: '',
    emittedAtUtc: new Date().toISOString(),
    payload: dto,
  };

  emitter.emitToTenantStaff({ tenantId, event: ChatSocketEvents.PresenceChanged, envelope });

  const portalUserIds = await audiences.findActivePortalUserIdsForPreparer(tenantId, parsed.userId);
  for (const userId of portalUserIds) {
    emitter.emitToUser({ tenantId, userId, event: ChatSocketEvents.PresenceChanged, envelope });
  }
}
