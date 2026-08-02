import type { Redis } from 'ioredis';
import { incrementAndGet } from './rate-counter.js';

/**
 * Leaky bucket 5 msg/s por (tenant, meeting, peer) para dominant-speaker.
 * Cierra el gap del plan §9C que exigia rate-limit anti-spam para audio-level.
 * Rate Limit Fase 0.4 — incremento atomico via `incrementAndGet` (antes INCR +
 * EXPIRE 1s como dos llamadas separadas): cuando el contador supera 5 en la
 * ventana, el mensaje se descarta silenciosamente.
 */
export class DominantSpeakerThrottle {
  private static readonly MAX_PER_SECOND = 5;

  constructor(private readonly redis: Redis) {}

  async allow(input: { tenantId: string; meetingId: string; userId: string }): Promise<boolean> {
    const key = `comm:ds:${input.tenantId}:${input.meetingId}:${input.userId}`;
    const count = await incrementAndGet(this.redis, key, 1);
    return count <= DominantSpeakerThrottle.MAX_PER_SECOND;
  }
}
