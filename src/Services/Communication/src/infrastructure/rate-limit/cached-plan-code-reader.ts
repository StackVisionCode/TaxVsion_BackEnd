import type { LimitsRepository } from '../../application/ports/settings-repository.js';

/**
 * Espejo de BuildingBlocks.Infrastructure.RateLimiting.CachedTenantPlanCodeReader (.NET): decorador
 * de cache en memoria (TTL 5 min) sobre `LimitsRepository.findByTenantId` — evita un round-trip a
 * Postgres por cada mensaje/evento de socket. `invalidate()` la llama
 * subscription-consumers.ts al vuelo cuando llega `subscription.entitlements_changed.v1`, en vez
 * de esperar el TTL (mismo criterio que el consumer .NET de Fase 6). Solo cachea resultados
 * positivos, igual que el original — un tenant desconocido no se cachea.
 */
const TTL_MS = 5 * 60 * 1000;

export class CachedPlanCodeReader {
  private readonly cache = new Map<string, { planCode: string; expiresAtMs: number }>();

  constructor(private readonly limits: LimitsRepository) {}

  async getPlanCode(tenantId: string): Promise<string | null> {
    const cached = this.cache.get(tenantId);
    if (cached && cached.expiresAtMs > Date.now()) return cached.planCode;

    const snapshot = await this.limits.findByTenantId(tenantId);
    if (!snapshot) return null;

    this.cache.set(tenantId, { planCode: snapshot.planCode, expiresAtMs: Date.now() + TTL_MS });
    return snapshot.planCode;
  }

  invalidate(tenantId: string): void {
    this.cache.delete(tenantId);
  }
}
