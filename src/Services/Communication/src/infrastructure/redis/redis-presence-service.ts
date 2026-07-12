import type { Redis } from 'ioredis';
import type { PresenceService } from '../../application/ports/presence-service.js';

/**
 * Presence en Redis:
 *   - Clave `comm:presence:t:{tenantId}:u:{userId}:s:{sessionId}` con lease TTL.
 *   - Al conectar, `register` con TTL (30s por default).
 *   - Heartbeat renueva TTL.
 *   - Al desconectar `unregister` borra la clave; si crash, expira sola.
 *   - `isOnline` = existe AL MENOS UNA clave `...:u:{userId}:s:*` (SCAN limitado).
 *
 * Cierre CRIT-4 legacy: nada de `sleep(2000)` ni 3 fuentes de verdad.
 *
 * Publica cambios via Pub/Sub `comm:presence:changed:{tenantId}` con
 * { userId, online, changedAtUtc } — otros pods lo consumen para emitir el
 * evento a los sockets suscritos a esa presencia.
 */
export class RedisPresenceService implements PresenceService {
  constructor(private readonly redis: Redis) {}

  async register(input: {
    tenantId: string;
    userId: string;
    sessionId: string;
    leaseSeconds: number;
  }): Promise<void> {
    // Order matters: SET first, then check if we're the only session key. If we
    // check before SET, two concurrent registers of the same user can both see
    // `wasOffline=true` and both publish 'online' → duplicate transitions.
    // With SET first, when the second register runs isOnline it finds the first
    // key already there → count >= 2 → doesn't publish again.
    await this.redis.set(this.sessionKey(input), '1', 'EX', input.leaseSeconds);
    const sessions = await this.countSessions(input.tenantId, input.userId);
    if (sessions <= 1) {
      await this.publishChange(input.tenantId, input.userId, true);
    }
  }

  private async countSessions(tenantId: string, userId: string): Promise<number> {
    const pattern = `comm:presence:t:${tenantId}:u:${userId}:s:*`;
    const [_cursor, keys] = await this.redis.scan(0, 'MATCH', pattern, 'COUNT', 25);
    return keys.length;
  }

  async heartbeat(input: {
    tenantId: string;
    userId: string;
    sessionId: string;
    leaseSeconds: number;
  }): Promise<void> {
    await this.redis.expire(this.sessionKey(input), input.leaseSeconds);
  }

  async unregister(input: { tenantId: string; userId: string; sessionId: string }): Promise<void> {
    await this.redis.del(this.sessionKey(input));
    if (!(await this.isOnline(input.tenantId, input.userId))) {
      await this.publishChange(input.tenantId, input.userId, false);
    }
  }

  async isOnline(tenantId: string, userId: string): Promise<boolean> {
    const pattern = `comm:presence:t:${tenantId}:u:${userId}:s:*`;
    // SCAN limitado — no bloqueamos Redis con KEYS.
    const [_cursor, keys] = await this.redis.scan(0, 'MATCH', pattern, 'COUNT', 25);
    return keys.length > 0;
  }

  async listOnline(tenantId: string, userIds: readonly string[]): Promise<readonly string[]> {
    if (userIds.length === 0) return [];
    const online: string[] = [];
    for (const userId of userIds) {
      if (await this.isOnline(tenantId, userId)) online.push(userId);
    }
    return online;
  }

  private sessionKey(input: { tenantId: string; userId: string; sessionId: string }): string {
    return `comm:presence:t:${input.tenantId}:u:${input.userId}:s:${input.sessionId}`;
  }

  private async publishChange(tenantId: string, userId: string, online: boolean): Promise<void> {
    const payload = { userId, online, changedAtUtc: new Date().toISOString() };
    await this.redis
      .publish(`comm:presence:changed:${tenantId}`, JSON.stringify(payload))
      .catch(() => undefined);
  }
}
