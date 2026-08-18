import { logger } from '../logger/logger.js';
import type { CachedPlanCodeReader } from './cached-plan-code-reader.js';
import type { HttpPlanRateLimitReader } from './http-plan-rate-limit-reader.js';

/**
 * Espejo de BuildingBlocks.RateLimiting.RateLimitQuotaResolver (.NET): resuelve el cupo efectivo
 * de un tenant para una categoria que escala por plan. Los 6 scopes de socket de Communication son
 * todos categoria "O" (ver domain/rate-limit/rate-limit-policies.ts) — mismo criterio .NET de que
 * solo el Bloque II-IV (F..O) escala, nunca A-E (pre-auth/publico/webhook, sin tenant).
 *
 * Fail-open en cada paso (flag apagado, plan no resuelto, catalogo no resuelto): cae a la cuota
 * base sin escalar, nunca lanza. Un hard-override reemplaza el cupo primario por completo, igual
 * que RateLimitQuotaResolver.cs — no se escala por ventana, se usa tal cual (Communication no tiene
 * overlay numerico para estos 6 scopes, asi que no aplica ese caso del original).
 */
export const TIER_SCALED_CATEGORY = 'O';

export class TierAwareQuotaResolver {
  constructor(
    private readonly planCodeReader: CachedPlanCodeReader,
    private readonly planRateLimitReader: HttpPlanRateLimitReader,
    private readonly enabled: boolean,
  ) {}

  async resolveMaxPerWindow(tenantId: string, baseMaxPerWindow: number): Promise<number> {
    if (!this.enabled) return baseMaxPerWindow;

    let planCode: string | null;
    try {
      planCode = await this.planCodeReader.getPlanCode(tenantId);
    } catch (error) {
      logger.warn({ error, tenantId }, 'tier-aware quota: plan code lookup failed; falling back to base quota');
      return baseMaxPerWindow;
    }
    if (!planCode) return baseMaxPerWindow;

    let snapshot;
    try {
      snapshot = await this.planRateLimitReader.getMultiplier(planCode, TIER_SCALED_CATEGORY);
    } catch (error) {
      logger.warn({ error, tenantId, planCode }, 'tier-aware quota: catalog lookup failed; falling back to base quota');
      return baseMaxPerWindow;
    }
    if (!snapshot) return baseMaxPerWindow;

    // Auditoria post-cierre: un hardOverridePerMinute <= 0 (dato corrupto o mal configurado en el
    // catalogo de Subscription) bloquearia el 100% del trafico del tenant en SocketRateLimiter.allow
    // (count arranca en 1) de forma silenciosa e indistinguible de abuso real. Mismo piso que la
    // rama del multiplicador de abajo.
    if (snapshot.hardOverridePerMinute !== null) return Math.max(1, snapshot.hardOverridePerMinute);

    return Math.max(1, Math.round(baseMaxPerWindow * snapshot.multiplierOverride));
  }
}
