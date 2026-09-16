import type { LimitsRepository } from '../../application/ports/settings-repository.js';

/**
 * Gate de modulo (log-only) — lector cacheado (TTL 5 min) de los modulos habilitados del plan del
 * tenant, sobre la misma proyeccion `TenantCommunicationLimits` que `CachedPlanCodeReader`.
 * `invalidate()` la llama `applyLimitsUpdate` al llegar `subscription.entitlements_changed.v1`, igual
 * que el cache de planCode. Devuelve `null` si aun no hay proyeccion para el tenant (evento no
 * consumido): el gate NO debe gatear en ese caso — `null` != "sin modulos", evita falsos "deny"
 * durante la consistencia eventual. Solo cachea positivos.
 */
const TTL_MS = 5 * 60 * 1000;

export class CachedTenantModulesReader {
  private readonly cache = new Map<string, { modules: readonly string[]; expiresAtMs: number }>();

  constructor(private readonly limits: LimitsRepository) {}

  async getEnabledModules(tenantId: string): Promise<readonly string[] | null> {
    const cached = this.cache.get(tenantId);
    if (cached && cached.expiresAtMs > Date.now()) return cached.modules;

    const snapshot = await this.limits.findByTenantId(tenantId);
    if (!snapshot) return null;

    this.cache.set(tenantId, { modules: snapshot.enabledModules, expiresAtMs: Date.now() + TTL_MS });
    return snapshot.enabledModules;
  }

  invalidate(tenantId: string): void {
    this.cache.delete(tenantId);
  }
}
