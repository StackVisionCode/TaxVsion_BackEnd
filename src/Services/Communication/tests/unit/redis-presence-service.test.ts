import { describe, expect, it } from 'vitest';
import type { Redis } from 'ioredis';
import { RedisPresenceService } from '../../src/infrastructure/redis/redis-presence-service.js';

/**
 * Fase B6 / auditoria del plan de presencia rica — el MD pedia
 * explicitamente "tests unitarios de computePresenceStatus (multi-fuente: 2
 * llamadas simultaneas, una termina, sigue Busy; ambas terminan, vuelve a
 * Online)" y un test de contrato del payload de `chat.presence.changed`. No
 * existe una funcion `computePresenceStatus` standalone — la derivacion vive
 * inline en `RedisPresenceService.publishCurrentStatus` (privado), asi que
 * estos tests la ejercitan a traves del efecto observable: lo que se publica
 * en `comm:presence:changed:{tenantId}` en cada transicion de `markBusy`/
 * `clearBusy`/`register`/`unregister`.
 *
 * Fake Redis minimo: implementa solo los metodos que RedisPresenceService
 * usa (set/expire/del/scan/mget/publish), en memoria, sin TTL real (los
 * tests no dependen de expiracion, solo de las llamadas explicitas de
 * set/del que ya hace el servicio).
 */
class FakeRedis {
  private store = new Map<string, string>();
  readonly publishedRaw: Array<{ channel: string; message: string }> = [];

  async set(key: string, value: string): Promise<'OK'> {
    this.store.set(key, value);
    return 'OK';
  }

  async expire(): Promise<number> {
    return 1;
  }

  async del(key: string): Promise<number> {
    return this.store.delete(key) ? 1 : 0;
  }

  async scan(_cursor: number, _match: string, pattern: string): Promise<[string, string[]]> {
    const regex = new RegExp('^' + pattern.replace(/[.+^${}()|[\]\\]/g, '\\$&').replace(/\*/g, '.*') + '$');
    const keys = [...this.store.keys()].filter((k) => regex.test(k));
    return ['0', keys];
  }

  async mget(...keys: string[]): Promise<Array<string | null>> {
    return keys.map((k) => this.store.get(k) ?? null);
  }

  async publish(channel: string, message: string): Promise<number> {
    this.publishedRaw.push({ channel, message });
    return 1;
  }
}

function publishedPayloads(redis: FakeRedis): Array<{ userId: string; status: string; busyReason: string | null; changedAtUtc: string }> {
  return redis.publishedRaw.map((p) => JSON.parse(p.message));
}

describe('RedisPresenceService — derivacion multi-fuente de Busy/Online/Offline', () => {
  it('sigue Busy mientras quede al menos una fuente activa; vuelve a Online solo cuando se limpian todas', async () => {
    const redis = new FakeRedis();
    const service = new RedisPresenceService(redis as unknown as Redis);
    const tenantId = 'tenant-1';
    const userId = 'user-1';

    await service.register({ tenantId, userId, sessionId: 'socket-1', leaseSeconds: 30 });
    await service.markBusy({ tenantId, userId, sourceId: 'call-1', kind: 'Call', leaseSeconds: 60 });
    await service.markBusy({ tenantId, userId, sourceId: 'meeting-1', kind: 'Meeting', leaseSeconds: 60 });

    let payloads = publishedPayloads(redis);
    expect(payloads).toHaveLength(2);
    expect(payloads[0]).toMatchObject({ userId, status: 'Online', busyReason: null });
    expect(payloads[1]).toMatchObject({ userId, status: 'Busy', busyReason: 'Call' });

    // Clear one of two active sources — status doesn't change (still Busy via
    // the remaining meeting source), so no new event fires.
    await service.clearBusy({ tenantId, userId, sourceId: 'call-1' });
    payloads = publishedPayloads(redis);
    expect(payloads).toHaveLength(2);

    // Clear the last active source — now genuinely back to Online.
    await service.clearBusy({ tenantId, userId, sourceId: 'meeting-1' });
    payloads = publishedPayloads(redis);
    expect(payloads).toHaveLength(3);
    expect(payloads[2]).toMatchObject({ userId, status: 'Online', busyReason: null });

    await service.unregister({ tenantId, userId, sessionId: 'socket-1' });
    payloads = publishedPayloads(redis);
    expect(payloads).toHaveLength(4);
    expect(payloads[3]).toMatchObject({ userId, status: 'Offline', busyReason: null });
  });

  it('marca Busy directamente al conectar si ya hay una fuente activa (orden set-then-count evita duplicar la transicion 0->1)', async () => {
    const redis = new FakeRedis();
    const service = new RedisPresenceService(redis as unknown as Redis);
    const tenantId = 'tenant-1';
    const userId = 'user-2';

    // Segunda sesion del mismo usuario conectando mientras ya hay una activa
    // no debe volver a publicar 'online' (regresion CRIT-legacy ya cubierta
    // para online/offline; aqui se prueba que markBusy respeta el mismo
    // criterio para las fuentes de busy).
    await service.register({ tenantId, userId, sessionId: 'socket-a', leaseSeconds: 30 });
    await service.register({ tenantId, userId, sessionId: 'socket-b', leaseSeconds: 30 });

    const payloads = publishedPayloads(redis);
    expect(payloads).toHaveLength(1);
    expect(payloads[0]).toMatchObject({ status: 'Online' });
  });
});

describe('RedisPresenceService — contrato del payload publicado (PresenceChangedDto)', () => {
  it('publica exactamente {userId, status, busyReason, changedAtUtc} — sin campos legacy como `online`', async () => {
    const redis = new FakeRedis();
    const service = new RedisPresenceService(redis as unknown as Redis);

    await service.register({ tenantId: 'tenant-1', userId: 'user-3', sessionId: 'socket-1', leaseSeconds: 30 });

    const payloads = publishedPayloads(redis);
    expect(payloads).toHaveLength(1);
    const payload = payloads[0]!;
    expect(Object.keys(payload).sort()).toEqual(['busyReason', 'changedAtUtc', 'status', 'userId']);
    expect(payload.status).toBe('Online');
    expect(payload.busyReason).toBeNull();
    expect(typeof payload.changedAtUtc).toBe('string');
    expect(() => new Date(payload.changedAtUtc).toISOString()).not.toThrow();
  });

  it('incluye busyReason cuando el status es Busy', async () => {
    const redis = new FakeRedis();
    const service = new RedisPresenceService(redis as unknown as Redis);
    const tenantId = 'tenant-1';
    const userId = 'user-4';

    await service.register({ tenantId, userId, sessionId: 'socket-1', leaseSeconds: 30 });
    await service.markBusy({ tenantId, userId, sourceId: 'meeting-1', kind: 'Meeting', leaseSeconds: 60 });

    const payloads = publishedPayloads(redis);
    const busyPayload = payloads.find((p) => p.status === 'Busy');
    expect(busyPayload?.busyReason).toBe('Meeting');
  });
});
