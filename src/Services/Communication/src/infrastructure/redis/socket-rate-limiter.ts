import type { Redis } from 'ioredis';

/**
 * Leaky bucket generico por (scope, tenant, user) — mismo patron que
 * `DominantSpeakerThrottle`, generalizado para cubrir el resto de eventos de
 * socket sin proteccion (SendMessage, TypingStart, EditMessage, Call.Initiate,
 * Call.Signal). Basado en INCR + EXPIRE: al superar `maxPerWindow` dentro de
 * `windowSeconds`, el evento se rechaza/descarta.
 */
export class SocketRateLimiter {
  constructor(private readonly redis: Redis) {}

  async allow(input: {
    scope: string;
    tenantId: string;
    userId: string;
    maxPerWindow: number;
    windowSeconds: number;
  }): Promise<boolean> {
    const key = `comm:rl:${input.scope}:${input.tenantId}:${input.userId}`;
    const count = await this.redis.incr(key);
    if (count === 1) {
      await this.redis.expire(key, input.windowSeconds);
    }
    return count <= input.maxPerWindow;
  }
}
