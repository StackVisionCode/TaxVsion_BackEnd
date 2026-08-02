import type { Redis } from 'ioredis';
import { incrementAndGet } from './rate-counter.js';
import { recordEvaluated, recordBlocked, recordFallbackOpen } from '../telemetry/rate-limit-metrics.js';

/**
 * RateLimit Fase 7 — reemplaza `@fastify/rate-limit` (in-memory, contado
 * por-instancia — con mas de una replica de Communication cada una lleva su
 * propio contador, dejando pasar N veces el limite real). Mismo patron atomico
 * que SocketRateLimiter (Fase 0.4), generico sobre la forma de la key: IP para
 * el limiter HTTP global, token/shortCode para las 2 rutas publicas de
 * meeting-invitations (ver rate-limit-policies.ts).
 *
 * Fase 8 — `policy` es obligatorio para poder etiquetar `ratelimit.evaluated_total`/`blocked_total`;
 * las 2 rutas de meeting-invitations pasan su nombre canonico de `rate-limit-policies.ts`, el
 * limiter global por IP (sin politica .NET equivalente, ver ese doc-comment) pasa un literal
 * sintetico. Estas rutas son publicas/pre-auth — sin tenant conocido, se etiqueta "n/a". Mismo
 * criterio fail-closed + `fallback_open_total` que `SocketRateLimiter` (ver su doc-comment).
 */
export class HttpRateLimiter {
  constructor(private readonly redis: Redis) {}

  async allow(input: { key: string; policy: string; maxPerWindow: number; windowSeconds: number }): Promise<boolean> {
    recordEvaluated(input.policy, 'http', 'n/a');

    let count: number;
    try {
      count = await incrementAndGet(this.redis, input.key, input.windowSeconds);
    } catch (error) {
      recordFallbackOpen(input.policy, 'redis_error');
      throw error;
    }

    const allowed = count <= input.maxPerWindow;
    if (!allowed) recordBlocked(input.policy, 'http', 'n/a');
    return allowed;
  }
}
