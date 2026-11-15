import { describe, expect, it } from 'vitest';
import { PeriodCallUsageMeter, usageMonthKey } from '../../src/application/services/call-usage-meter.js';
import type { TenantCommunicationLimitsSnapshot } from '../../src/domain/settings/tenant-communication-limits.js';

// P11: el cupo de minutos se reinicia el día de alta de la suscripción, a medianoche de la oficina.
const anchorOn15th = new Date('2026-03-15T14:00:00Z'); // 10:00 del 15-mar en Nueva York

describe('usageMonthKey', () => {
  it('before the anchor day the period is still the one that started last month', () => {
    expect(usageMonthKey(new Date('2026-11-14T12:00:00Z'), anchorOn15th, 'America/New_York')).toBe('2026-10');
  });

  it('a new period starts at local midnight of the anchor day', () => {
    expect(usageMonthKey(new Date('2026-11-15T05:00:00Z'), anchorOn15th, 'America/New_York')).toBe('2026-11');
    expect(usageMonthKey(new Date('2026-11-15T04:59:59Z'), anchorOn15th, 'America/New_York')).toBe('2026-10');
  });

  it('an anchor on the 31st uses the last day of shorter months', () => {
    const anchor = new Date('2027-01-31T15:00:00Z');
    expect(usageMonthKey(new Date('2027-02-28T12:00:00Z'), anchor, 'America/New_York')).toBe('2027-02');
    expect(usageMonthKey(new Date('2027-02-27T12:00:00Z'), anchor, 'America/New_York')).toBe('2027-01');
  });

  it('without an anchor it is the UTC month, as before', () => {
    expect(usageMonthKey(new Date('2026-12-01T03:00:00Z'), null, 'America/New_York')).toBe('2026-12');
  });
});

describe('PeriodCallUsageMeter', () => {
  it('stores and reads seconds under the office period of the tenant', async () => {
    const stored: Array<{ tenantId: string; yearMonth: string; seconds: number }> = [];
    const meter = new PeriodCallUsageMeter({
      usage: {
        addSeconds: async (i) => {
          stored.push(i);
        },
        getSeconds: async (i) => (i.yearMonth === '2026-10' ? 120 : 0),
      },
      limits: { findByTenantId: async () => ({ usagePeriodAnchorUtc: anchorOn15th }) as TenantCommunicationLimitsSnapshot },
      timeZones: { find: async (tenantId) => ({ tenantId, timeZoneId: 'America/New_York', sourceVersion: 1, sourceUpdatedAtUtc: new Date() }) },
    });

    const at = new Date('2026-11-14T12:00:00Z');
    await meter.addSeconds({ tenantId: 't-1', at, seconds: 60 });

    expect(stored).toEqual([{ tenantId: 't-1', yearMonth: '2026-10', seconds: 60 }]);
    expect(await meter.secondsThisPeriod({ tenantId: 't-1', at })).toBe(120);
  });
});
