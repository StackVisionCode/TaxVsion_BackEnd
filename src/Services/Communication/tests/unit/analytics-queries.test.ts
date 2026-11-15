import { describe, expect, it, vi } from 'vitest';
import { analyticsSummary, analyticsTimeline } from '../../src/application/use-cases/analytics-queries.js';
import type { AnalyticsRepository } from '../../src/application/ports/analytics-repository.js';

// TZ03-H3: los días del snapshot se cortan en analyticsTimeZone; "hoy" del rango por defecto también.
describe('analytics queries default range', () => {
  const deps = () => ({
    analytics: { listForRange: vi.fn().mockResolvedValue([]) } as unknown as AnalyticsRepository,
    timeZone: async () => 'America/New_York',
    // 03:00Z del 14-nov = 22:00 del 13-nov en Nueva York.
    now: () => new Date('2026-11-14T03:00:00Z'),
  });

  it('summary ends on the analytics-zone day, not the UTC day', async () => {
    const r = await analyticsSummary({ tenantId: 't-1' }, deps());
    expect([r.fromDay, r.toDay]).toEqual(['2026-10-15', '2026-11-13']);
  });

  it('timeline ends on the analytics-zone day, not the UTC day', async () => {
    const r = await analyticsTimeline({ tenantId: 't-1' }, deps());
    expect([r.fromDay, r.toDay]).toEqual(['2026-10-15', '2026-11-13']);
  });
});
