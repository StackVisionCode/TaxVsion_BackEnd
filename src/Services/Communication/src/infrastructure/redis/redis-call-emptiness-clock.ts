import type { Redis } from 'ioredis';
import type { CallEmptinessClock } from '../../application/ports/abandoned-call-ports.js';

const KEY_PREFIX = 'comm:call:empty-since:';
/** La clave caduca sola si la llamada se cerró por otro camino y nadie la limpió. */
const TTL_SECONDS = 24 * 60 * 60;

export class RedisCallEmptinessClock implements CallEmptinessClock {
  constructor(private readonly redis: Redis) {}

  async emptySince(callId: string, nowMs: number): Promise<number> {
    const key = `${KEY_PREFIX}${callId}`;
    await this.redis.set(key, String(nowMs), 'EX', TTL_SECONDS, 'NX');
    const stored = Number(await this.redis.get(key));
    return Number.isFinite(stored) && stored > 0 ? stored : nowMs;
  }

  async clear(callId: string): Promise<void> {
    await this.redis.del(`${KEY_PREFIX}${callId}`);
  }
}
