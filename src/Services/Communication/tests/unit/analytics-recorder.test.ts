import { describe, expect, it, vi } from 'vitest';
import { AnalyticsRecorder, dayInTimeZone } from '../../src/application/services/analytics-recorder.js';
import type { AnalyticsRepository } from '../../src/application/ports/analytics-repository.js';

function repo() {
  return {
    incrementCounters: vi.fn().mockResolvedValue(undefined),
    raisePeaks: vi.fn().mockResolvedValue(undefined),
    listForRange: vi.fn(),
    listAllForRange: vi.fn(),
    purgeBefore: vi.fn(),
  } satisfies AnalyticsRepository;
}

/** F10: el día y el plan nunca hacen perder un contador. */
describe('AnalyticsRecorder', () => {
  it('dayInTimeZone corta el día en la zona dada y cae a UTC con una zona inválida', () => {
    expect(dayInTimeZone('2026-10-31T02:00:00.000Z', 'America/New_York')).toBe('2026-10-30');
    expect(dayInTimeZone('2026-10-31T23:30:00.000Z', 'Asia/Tokyo')).toBe('2026-11-01');
    expect(dayInTimeZone('2026-10-31T02:00:00.000Z', 'Not/AZone')).toBe('2026-10-31');
  });

  it('si la zona o el plan no se pueden leer, cuenta igual en UTC y sin plan', async () => {
    const analytics = repo();
    const recorder = new AnalyticsRecorder({
      analytics,
      timeZone: () => Promise.reject(new Error('redis down')),
      planCodeOf: () => Promise.reject(new Error('db down')),
    });
    await recorder.count('t-1', '2026-10-31T02:00:00.000Z', { messagesSent: 1 });
    expect(analytics.incrementCounters).toHaveBeenCalledWith({ tenantId: 't-1', day: '2026-10-31', increments: { messagesSent: 1 }, planCode: null });
  });

  it('sin lector de concurrencia no intenta subir picos', async () => {
    const analytics = repo();
    await new AnalyticsRecorder({ analytics, timeZone: async () => 'UTC' }).observePeaks('t-1', '2026-10-31T00:00:00Z');
    expect(analytics.raisePeaks).not.toHaveBeenCalled();
  });
});
