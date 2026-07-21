import type { Redis } from 'ioredis';
import type { PresenceService } from '../../application/ports/presence-service.js';
import { isBusyReason, type BusyReason, type PresenceStatus } from '../../domain/presence/presence-status.js';

/**
 * Presence en Redis:
 *   - Clave `comm:presence:t:{tenantId}:u:{userId}:s:{sessionId}` con lease TTL
 *     (online/offline — sesiones de socket).
 *   - Clave `comm:presence:busy:t:{tenantId}:u:{userId}:src:{sourceId}` con
 *     lease TTL (Busy — fuentes activas: una Call o un Meeting concretos).
 *   - Al conectar, `register` con TTL (30s por default). Heartbeat renueva TTL.
 *   - Al desconectar `unregister` borra la clave; si crash, expira sola.
 *   - `isOnline` = existe AL MENOS UNA clave `...:u:{userId}:s:*` (SCAN limitado).
 *
 * Cierre CRIT-4 legacy: nada de `sleep(2000)` ni 3 fuentes de verdad.
 *
 * Publica cambios via Pub/Sub `comm:presence:changed:{tenantId}` con
 * { userId, status, busyReason, changedAtUtc } — otros pods lo consumen para
 * emitir el evento a los sockets suscritos a esa presencia. `status` deriva
 * de ambos ejes: Offline si no hay sesiones, Busy si hay sesiones y al menos
 * una fuente activa, Online en otro caso.
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
      await this.publishCurrentStatus(input.tenantId, input.userId);
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
      await this.publishCurrentStatus(input.tenantId, input.userId);
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

  async markBusy(input: {
    tenantId: string;
    userId: string;
    sourceId: string;
    kind: BusyReason;
    leaseSeconds: number;
  }): Promise<void> {
    // Mismo orden SET-then-count que register(), por la misma razon: evita
    // que dos markBusy concurrentes (ej. Call + Meeting casi simultaneos)
    // publiquen dos veces la transicion 0->1.
    await this.redis.set(this.busyKey(input), input.kind, 'EX', input.leaseSeconds);
    const sources = await this.countBusySources(input.tenantId, input.userId);
    if (sources.count <= 1) {
      await this.publishCurrentStatus(input.tenantId, input.userId);
    }
  }

  async clearBusy(input: { tenantId: string; userId: string; sourceId: string }): Promise<void> {
    await this.redis.del(this.busyKey(input));
    const sources = await this.countBusySources(input.tenantId, input.userId);
    if (sources.count === 0) {
      await this.publishCurrentStatus(input.tenantId, input.userId);
    }
  }

  private async countBusySources(
    tenantId: string,
    userId: string,
  ): Promise<{ count: number; firstReason: BusyReason | null }> {
    const pattern = `comm:presence:busy:t:${tenantId}:u:${userId}:src:*`;
    const [_cursor, keys] = await this.redis.scan(0, 'MATCH', pattern, 'COUNT', 25);
    if (keys.length === 0) return { count: 0, firstReason: null };
    const values = await this.redis.mget(...keys);
    const firstReason = values.find((v): v is BusyReason => v !== null && isBusyReason(v)) ?? null;
    return { count: keys.length, firstReason };
  }

  private sessionKey(input: { tenantId: string; userId: string; sessionId: string }): string {
    return `comm:presence:t:${input.tenantId}:u:${input.userId}:s:${input.sessionId}`;
  }

  private busyKey(input: { tenantId: string; userId: string; sourceId: string }): string {
    return `comm:presence:busy:t:${input.tenantId}:u:${input.userId}:src:${input.sourceId}`;
  }

  private async publishCurrentStatus(tenantId: string, userId: string): Promise<void> {
    const online = await this.isOnline(tenantId, userId);
    const busy = online ? await this.countBusySources(tenantId, userId) : { count: 0, firstReason: null };
    const status: PresenceStatus = !online ? 'Offline' : busy.count > 0 ? 'Busy' : 'Online';
    const payload = {
      userId,
      status,
      busyReason: status === 'Busy' ? busy.firstReason : null,
      changedAtUtc: new Date().toISOString(),
    };
    await this.redis
      .publish(`comm:presence:changed:${tenantId}`, JSON.stringify(payload))
      .catch(() => undefined);
  }
}
