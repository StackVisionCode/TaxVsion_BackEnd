import type { Redis } from 'ioredis';
import { incrementAndGet } from './rate-counter.js';
import { recordEvaluated, recordBlocked, recordFallbackOpen } from '../telemetry/rate-limit-metrics.js';

/**
 * Leaky bucket generico por (scope, tenant, user) — mismo patron que
 * `DominantSpeakerThrottle`, generalizado para cubrir el resto de eventos de
 * socket sin proteccion (SendMessage, TypingStart, EditMessage, Call.Initiate,
 * Call.Signal). Rate Limit Fase 0.4 — incremento atomico via `incrementAndGet`
 * (antes INCR + EXPIRE como dos llamadas separadas): al superar `maxPerWindow`
 * dentro de `windowSeconds`, el evento se rechaza/descarta.
 *
 * Fase 8 — emite `ratelimit.evaluated_total`/`blocked_total` etiquetados `layer: "socket"`
 * (categoria O del catalogo .NET no tiene overlay, una sola capa siempre). A diferencia del lado
 * .NET, Redis caido hoy propaga la excepcion (fail-CLOSED, no fail-open — gap preexistente, no
 * corregido en esta fase para no cambiar comportamiento de produccion fuera del alcance de
 * observabilidad) — igual se emite `fallback_open_total` con reason "redis_error" antes de
 * relanzar, para poder ver cuantas veces pasa esto en Grafana.
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
    recordEvaluated(input.scope, 'socket', input.tenantId);

    let count: number;
    try {
      count = await incrementAndGet(this.redis, key, input.windowSeconds);
    } catch (error) {
      recordFallbackOpen(input.scope, 'redis_error');
      throw error;
    }

    const allowed = count <= input.maxPerWindow;
    if (!allowed) recordBlocked(input.scope, 'socket', input.tenantId);
    return allowed;
  }
}
