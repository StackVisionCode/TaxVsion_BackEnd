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
 * (categoria O del catalogo .NET no tiene overlay, una sola capa siempre).
 *
 * Auditoria RateLimit hallazgo #3 — antes de esto, Redis caido relanzaba la excepcion (fail-CLOSED
 * disfrazado de fail-open por el nombre de la metrica), al reves de `TieredRateLimitEvaluator`
 * (.NET) y del ADR_017 (Redis caido nunca debe bloquear trafico). Ahora, igual que el lado .NET,
 * se registra `fallback_open_total{reason=redis_error}` y se permite el evento.
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
      return true;
    }

    const allowed = count <= input.maxPerWindow;
    if (!allowed) recordBlocked(input.scope, 'socket', input.tenantId);
    return allowed;
  }
}
