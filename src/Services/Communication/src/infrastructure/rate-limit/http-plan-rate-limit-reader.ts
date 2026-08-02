import { config } from '../config.js';
import { logger } from '../logger/logger.js';
import type { ServiceTokenClient } from '../auth/service-token-client.js';

/**
 * Espejo de BuildingBlocks.Infrastructure.RateLimiting.HttpPlanRateLimitReader (.NET): trae el
 * catalogo completo de PlanRateLimits desde Subscription (GET subscriptions/internal/plan-rate-limits)
 * y lo cachea 5 min en memoria — el catalogo es global (no por-tenant), asi que una sola llamada
 * M2M cubre todos los tenants del proceso. Usa `config.platformTenantId` como sentinel de tenant
 * para el token M2M, mismo criterio que HttpPlanRateLimitReader.cs (Guid.Empty es rechazado por
 * Auth incondicionalmente).
 */
export interface PlanRateLimitSnapshot {
  readonly multiplierOverride: number;
  readonly hardOverridePerMinute: number | null;
}

interface RawPlanRateLimitRow {
  readonly planCode?: unknown;
  readonly PlanCode?: unknown;
  readonly category?: unknown;
  readonly Category?: unknown;
  readonly multiplierOverride?: unknown;
  readonly MultiplierOverride?: unknown;
  readonly hardOverridePerMinute?: unknown;
  readonly HardOverridePerMinute?: unknown;
}

const CATALOG_TTL_MS = 5 * 60 * 1000;

export class HttpPlanRateLimitReader {
  private catalog: Map<string, PlanRateLimitSnapshot> | null = null;
  private catalogExpiresAtMs = 0;
  private inFlight: Promise<Map<string, PlanRateLimitSnapshot>> | null = null;

  constructor(private readonly tokens: ServiceTokenClient) {}

  async getMultiplier(planCode: string, category: string): Promise<PlanRateLimitSnapshot | null> {
    const catalog = await this.getCatalog();
    return catalog.get(catalogKey(planCode, category)) ?? null;
  }

  private async getCatalog(): Promise<Map<string, PlanRateLimitSnapshot>> {
    if (this.catalog && this.catalogExpiresAtMs > Date.now()) return this.catalog;
    if (this.inFlight) return this.inFlight;

    this.inFlight = this.fetchCatalog();
    try {
      const catalog = await this.inFlight;
      this.catalog = catalog;
      this.catalogExpiresAtMs = Date.now() + CATALOG_TTL_MS;
      return catalog;
    } finally {
      this.inFlight = null;
    }
  }

  // Fail-open siempre: cualquier fallo (token, red, status, parseo) devuelve catalogo vacio, que
  // hace que getMultiplier() resuelva null y el caller caiga a la cuota base sin escalar — nunca
  // un 500 por esto.
  private async fetchCatalog(): Promise<Map<string, PlanRateLimitSnapshot>> {
    const empty = new Map<string, PlanRateLimitSnapshot>();

    let token: string;
    try {
      token = await this.tokens.getToken(config.platformTenantId);
    } catch (error) {
      logger.warn({ error }, 'could not acquire service token for plan-rate-limits catalog; failing open');
      return empty;
    }

    let response: Response;
    try {
      response = await fetch(`${config.subscription.baseUrl}/subscriptions/internal/plan-rate-limits`, {
        headers: { authorization: `Bearer ${token}` },
      });
    } catch (error) {
      logger.warn({ error }, 'plan-rate-limits catalog request failed; failing open');
      return empty;
    }

    if (!response.ok) {
      logger.warn({ status: response.status }, 'plan-rate-limits catalog request failed; failing open');
      return empty;
    }

    const rows = (await response.json().catch(() => [])) as RawPlanRateLimitRow[];
    const catalog = new Map<string, PlanRateLimitSnapshot>();
    for (const row of rows) {
      const planCode = pick(row.planCode, row.PlanCode);
      const category = pick(row.category, row.Category);
      const multiplierOverride = pick(row.multiplierOverride, row.MultiplierOverride);
      const hardOverridePerMinute = pick(row.hardOverridePerMinute, row.HardOverridePerMinute);
      if (typeof planCode !== 'string' || typeof category !== 'string' || typeof multiplierOverride !== 'number') {
        continue;
      }
      catalog.set(catalogKey(planCode, category), {
        multiplierOverride,
        hardOverridePerMinute: typeof hardOverridePerMinute === 'number' ? hardOverridePerMinute : null,
      });
    }
    return catalog;
  }
}

function pick(camel: unknown, pascal: unknown): unknown {
  return camel !== undefined ? camel : pascal;
}

function catalogKey(planCode: string, category: string): string {
  return `${planCode}:${category}`;
}
