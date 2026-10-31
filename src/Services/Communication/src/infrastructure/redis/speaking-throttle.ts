import type { Redis } from 'ioredis';
import { incrementAndGet } from './rate-counter.js';
import { TECHNICAL_LIMITS, clampToTechnical } from '../../domain/settings/technical-limits.js';

/**
 * F5 (T-69): tope de `meeting.speaking` por (tenant, meeting, usuario) y segundo. Sustituye al
 * throttle de `meeting.dominant_speaker` (retirado: la UI detecta al hablante en local y el servidor
 * solo registra la linea de tiempo). El tope lo decide la plataforma (`media.speaking.maxPerSecond`,
 * columna `DominantSpeakerMaxPerSecond`); aqui solo se acota al rango tecnico. Incremento atomico
 * (Rate Limit Fase 0.4): por encima del tope en la ventana de 1 s el mensaje se descarta.
 */
export class SpeakingThrottle {
  constructor(private readonly redis: Redis) {}

  async allow(
    input: { tenantId: string; meetingId: string; userId: string },
    maxPerSecond: number = TECHNICAL_LIMITS.dominantSpeakerMaxPerSecond.fallback,
  ): Promise<boolean> {
    const key = `comm:ds:${input.tenantId}:${input.meetingId}:${input.userId}`;
    const count = await incrementAndGet(this.redis, key, 1);
    return count <= clampToTechnical('dominantSpeakerMaxPerSecond', maxPerSecond);
  }
}
