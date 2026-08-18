import { describe, expect, it } from 'vitest';
import { TierAwareQuotaResolver } from '../../src/infrastructure/rate-limit/tier-aware-quota-resolver.js';
import type { CachedPlanCodeReader } from '../../src/infrastructure/rate-limit/cached-plan-code-reader.js';
import type {
  HttpPlanRateLimitReader,
  PlanRateLimitSnapshot,
} from '../../src/infrastructure/rate-limit/http-plan-rate-limit-reader.js';

function fakePlanCodeReader(planCode: string | null): CachedPlanCodeReader {
  return { getPlanCode: async () => planCode } as unknown as CachedPlanCodeReader;
}

function fakePlanRateLimitReader(snapshot: PlanRateLimitSnapshot | null): HttpPlanRateLimitReader {
  return { getMultiplier: async () => snapshot } as unknown as HttpPlanRateLimitReader;
}

describe('TierAwareQuotaResolver.resolveMaxPerWindow', () => {
  it('returns the base quota untouched when the flag is disabled', async () => {
    const resolver = new TierAwareQuotaResolver(fakePlanCodeReader('pro'), fakePlanRateLimitReader(null), false);
    await expect(resolver.resolveMaxPerWindow('tenant-1', 30)).resolves.toBe(30);
  });

  it('falls back to the base quota when the tenant has no plan code resolved', async () => {
    const resolver = new TierAwareQuotaResolver(fakePlanCodeReader(null), fakePlanRateLimitReader(null), true);
    await expect(resolver.resolveMaxPerWindow('tenant-1', 30)).resolves.toBe(30);
  });

  it('falls back to the base quota when the catalog has no row for the plan/category', async () => {
    const resolver = new TierAwareQuotaResolver(fakePlanCodeReader('pro'), fakePlanRateLimitReader(null), true);
    await expect(resolver.resolveMaxPerWindow('tenant-1', 30)).resolves.toBe(30);
  });

  it('scales the base quota by the multiplier, rounded', async () => {
    const resolver = new TierAwareQuotaResolver(
      fakePlanCodeReader('pro'),
      fakePlanRateLimitReader({ multiplierOverride: 3, hardOverridePerMinute: null }),
      true,
    );
    await expect(resolver.resolveMaxPerWindow('tenant-1', 30)).resolves.toBe(90);
  });

  it('never returns less than 1 even when the multiplier rounds a tiny base quota down to 0', async () => {
    const resolver = new TierAwareQuotaResolver(
      fakePlanCodeReader('starter'),
      fakePlanRateLimitReader({ multiplierOverride: 0.1, hardOverridePerMinute: null }),
      true,
    );
    await expect(resolver.resolveMaxPerWindow('tenant-1', 1)).resolves.toBe(1);
  });

  it('uses the hard override as-is when present', async () => {
    const resolver = new TierAwareQuotaResolver(
      fakePlanCodeReader('enterprise'),
      fakePlanRateLimitReader({ multiplierOverride: 1, hardOverridePerMinute: 500 }),
      true,
    );
    await expect(resolver.resolveMaxPerWindow('tenant-1', 30)).resolves.toBe(500);
  });

  // Regresion — auditoria post-cierre: un hardOverridePerMinute <= 0 (dato corrupto/mal
  // configurado en el catalogo de Subscription) bloqueaba el 100% del trafico del tenant en
  // SocketRateLimiter.allow (count arranca en 1) de forma silenciosa. Debe clampear a 1, igual
  // que la rama del multiplicador.
  it('clamps a non-positive hard override to 1 instead of blocking all traffic', async () => {
    const resolver = new TierAwareQuotaResolver(
      fakePlanCodeReader('enterprise'),
      fakePlanRateLimitReader({ multiplierOverride: 1, hardOverridePerMinute: 0 }),
      true,
    );
    await expect(resolver.resolveMaxPerWindow('tenant-1', 30)).resolves.toBe(1);
  });

  it('clamps a negative hard override to 1', async () => {
    const resolver = new TierAwareQuotaResolver(
      fakePlanCodeReader('enterprise'),
      fakePlanRateLimitReader({ multiplierOverride: 1, hardOverridePerMinute: -5 }),
      true,
    );
    await expect(resolver.resolveMaxPerWindow('tenant-1', 30)).resolves.toBe(1);
  });

  it('falls back to the base quota when the plan code lookup throws', async () => {
    const throwingPlanCodeReader = {
      getPlanCode: async () => {
        throw new Error('db down');
      },
    } as unknown as CachedPlanCodeReader;
    const resolver = new TierAwareQuotaResolver(throwingPlanCodeReader, fakePlanRateLimitReader(null), true);
    await expect(resolver.resolveMaxPerWindow('tenant-1', 30)).resolves.toBe(30);
  });

  it('falls back to the base quota when the catalog lookup throws', async () => {
    const throwingReader = {
      getMultiplier: async () => {
        throw new Error('network down');
      },
    } as unknown as HttpPlanRateLimitReader;
    const resolver = new TierAwareQuotaResolver(fakePlanCodeReader('pro'), throwingReader, true);
    await expect(resolver.resolveMaxPerWindow('tenant-1', 30)).resolves.toBe(30);
  });
});
