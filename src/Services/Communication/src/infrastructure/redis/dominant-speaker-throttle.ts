import type { Redis } from 'ioredis';

/**
 * Leaky bucket 5 msg/s por (tenant, meeting, peer) para dominant-speaker.
 * Cierra el gap del plan §9C que exigia rate-limit anti-spam para audio-level.
 * Basado en INCR + EXPIRE 1s: cuando el contador supera 5 en la ventana, el
 * mensaje se descarta silenciosamente.
 */
export class DominantSpeakerThrottle {
  private static readonly MAX_PER_SECOND = 5;

  constructor(private readonly redis: Redis) {}

  async allow(input: { tenantId: string; meetingId: string; userId: string }): Promise<boolean> {
    const key = `comm:ds:${input.tenantId}:${input.meetingId}:${input.userId}`;
    const count = await this.redis.incr(key);
    if (count === 1) {
      await this.redis.expire(key, 1);
    }
    return count <= DominantSpeakerThrottle.MAX_PER_SECOND;
  }
}
