import type { Redis } from 'ioredis';
import type { InstanceSnapshot, InstanceTelemetryStore } from '../../application/ports/instance-telemetry-store.js';

const KEY_PREFIX = 'comm:instance:';

/** Una clave por proceso con TTL; `list` recorre con SCAN (nunca KEYS) y descarta lo ilegible. */
export class RedisInstanceTelemetryStore implements InstanceTelemetryStore {
  constructor(private readonly redis: Redis) {}

  async publish(snapshot: InstanceSnapshot, ttlSeconds: number): Promise<void> {
    await this.redis.set(`${KEY_PREFIX}${snapshot.instanceId}`, JSON.stringify(snapshot), 'EX', ttlSeconds);
  }

  async list(): Promise<readonly InstanceSnapshot[]> {
    const keys: string[] = [];
    let cursor = '0';
    do {
      const [next, batch] = await this.redis.scan(cursor, 'MATCH', `${KEY_PREFIX}*`, 'COUNT', 100);
      cursor = next;
      keys.push(...batch);
    } while (cursor !== '0');
    if (keys.length === 0) return [];
    const values = await this.redis.mget(...keys);
    const snapshots: InstanceSnapshot[] = [];
    for (const value of values) {
      if (!value) continue;
      try {
        snapshots.push(JSON.parse(value) as InstanceSnapshot);
      } catch {
        // Un valor corrupto no tumba la consulta: ese proceso vuelve a publicar en su próximo latido.
      }
    }
    return snapshots.sort((a, b) => a.instanceId.localeCompare(b.instanceId));
  }
}
