import { describe, expect, it, vi } from 'vitest';
import {
  auditPlatformAnalyticsQuery,
  bucketOf,
  platformAnalyticsSummary,
  platformAnalyticsTenants,
  type PlatformAnalyticsDeps,
} from '../../src/application/use-cases/platform-analytics-queries.js';
import { ANALYTICS_COUNTERS, type AnalyticsSnapshotRow } from '../../src/application/ports/analytics-repository.js';

const A = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa';
const B = 'bbbbbbbb-bbbb-4bbb-8bbb-bbbbbbbbbbbb';

function row(tenantId: string, day: string, over: Partial<AnalyticsSnapshotRow> = {}): AnalyticsSnapshotRow {
  const zeros = Object.fromEntries(ANALYTICS_COUNTERS.map((k) => [k, 0]));
  return { ...(zeros as unknown as AnalyticsSnapshotRow), peakConcurrentMeetings: 0, peakConcurrentParticipants: 0, tenantId, day, planCode: 'enterprise', ...over };
}

function deps(rows: AnalyticsSnapshotRow[], over: Partial<PlatformAnalyticsDeps> = {}): PlatformAnalyticsDeps & { listAllForRange: ReturnType<typeof vi.fn> } {
  const listAllForRange = vi.fn().mockResolvedValue(rows);
  return {
    analytics: { listAllForRange },
    timeZone: async () => 'UTC',
    defaultRangeDays: 30,
    maxRangeDays: 400,
    now: () => new Date('2026-10-31T12:00:00Z'),
    listAllForRange,
    ...over,
  };
}

/** F10 (ADD-08 T-72): agregación de plataforma en el servidor sobre el snapshot. */
describe('bucketOf', () => {
  it.each([
    ['2026-01-01', '2025-12-29', '2026-W01'], // jueves: la semana 1 de 2026 empieza el lunes 29/12
    ['2027-01-01', '2026-12-28', '2026-W53'], // viernes: 2026 tiene 53 semanas ISO
    ['2024-12-30', '2024-12-30', '2025-W01'], // lunes que ya es del año ISO siguiente
    ['2026-11-01', '2026-10-26', '2026-W44'], // domingo: pertenece a la semana del lunes anterior
  ])('semana ISO de %s → lunes %s, %s', (day, period, label) => {
    expect(bucketOf(day, 'week')).toEqual({ period, label });
  });

  it('mes y día', () => {
    expect(bucketOf('2026-10-31', 'month')).toEqual({ period: '2026-10-01', label: '2026-10' });
    expect(bucketOf('2026-10-31', 'day')).toEqual({ period: '2026-10-31', label: '2026-10-31' });
  });
});

describe('platformAnalyticsSummary', () => {
  it('suma todas las oficinas por mes; los picos son el máximo de una oficina, no la suma', async () => {
    const d = deps([
      row(A, '2026-09-30', { callsEnded: 1, callSeconds: 30, callsCompleted: 1, peakConcurrentParticipants: 4 }),
      row(A, '2026-10-01', { callsEnded: 2, callSeconds: 100, callsCompleted: 1, callsFailed: 1, peakConcurrentParticipants: 6 }),
      row(B, '2026-10-02', { meetingsStarted: 2, participantsJoined: 7, guestsJoined: 1, peakConcurrentParticipants: 5 }),
    ]);
    const result = await platformAnalyticsSummary({ fromDay: '2026-09-01', toDay: '2026-10-31', groupBy: 'month' }, d);
    expect(result.isSuccess).toBe(true);
    const v = result.isSuccess ? result.value : null;
    expect(v?.periods.map((p) => [p.label, p.activeTenants, p.totals.callSeconds, p.peaks.peakConcurrentParticipants])).toEqual([
      ['2026-09', 1, 30, 4],
      ['2026-10', 2, 100, 6],
    ]);
    expect(v?.totals.callsEnded).toBe(3);
    expect(v?.activeTenants).toBe(2);
    expect(v?.peaks.peakConcurrentParticipants).toBe(6);
    expect(v?.derived).toMatchObject({ averageCallSeconds: 65, callFailureRate: 0.3333, averageParticipantsPerMeeting: 3.5, guestShare: 0.1429 });
  });

  it('pasa los filtros de oficina y plan al repositorio', async () => {
    const d = deps([]);
    await platformAnalyticsSummary({ fromDay: '2026-10-01', toDay: '2026-10-31', tenantId: A, planCode: 'starter' }, d);
    expect(d.listAllForRange).toHaveBeenCalledWith({ fromDay: '2026-10-01', toDay: '2026-10-31', tenantId: A, planCode: 'starter' });
  });

  it('sin rango usa los últimos N días hasta hoy en la zona de la plataforma', async () => {
    // 02:00 UTC del 1/11 es todavía el 31/10 en Nueva York.
    const d = deps([], { timeZone: async () => 'America/New_York', now: () => new Date('2026-11-01T02:00:00Z') });
    const result = await platformAnalyticsSummary({}, d);
    expect(result.isSuccess && [result.value.fromDay, result.value.toDay, result.value.timeZone]).toEqual([
      '2026-10-02',
      '2026-10-31',
      'America/New_York',
    ]);
  });

  it('rechaza rangos invertidos, mal formados o más largos que el tope', async () => {
    const d = deps([]);
    const inverted = await platformAnalyticsSummary({ fromDay: '2026-10-31', toDay: '2026-10-01' }, d);
    const malformed = await platformAnalyticsSummary({ fromDay: '2026-02-30', toDay: '2026-03-01' }, d);
    const tooLong = await platformAnalyticsSummary({ fromDay: '2024-01-01', toDay: '2026-10-31' }, d);
    expect([inverted, malformed, tooLong].map((r) => (r.isSuccess ? 'ok' : r.error.code))).toEqual([
      'Analytics.InvalidRange',
      'Analytics.InvalidRange',
      'Analytics.RangeTooLarge',
    ]);
  });
});

describe('platformAnalyticsTenants', () => {
  it('una fila por oficina con su plan más reciente, ordenada por uso y recortada al límite', async () => {
    const d = deps([
      row(A, '2026-10-01', { callSeconds: 10, planCode: 'starter' }),
      row(B, '2026-10-01', { meetingSeconds: 500, planCode: 'starter' }),
      row(B, '2026-10-05', { meetingSeconds: 100, planCode: 'enterprise' }),
    ]);
    const result = await platformAnalyticsTenants({ fromDay: '2026-10-01', toDay: '2026-10-31', limit: 1 }, d);
    const v = result.isSuccess ? result.value : null;
    expect(v?.total).toBe(2);
    expect(v?.tenants.map((t) => [t.tenantId, t.planCode, t.activeDays, t.totals.meetingSeconds])).toEqual([[B, 'enterprise', 2, 600]]);
  });
});

describe('auditPlatformAnalyticsQuery', () => {
  it('deja una fila AdminQuery con actor y filtros (sin vacíos), sin datos de la respuesta', async () => {
    const append = vi.fn().mockResolvedValue(undefined);
    await auditPlatformAnalyticsQuery(
      { endpoint: 'summary', actorUserId: A, params: { from: '2026-10-01', planCode: undefined, groupBy: 'week' }, correlationId: 'req-1' },
      { audit: { append } },
    );
    expect(append.mock.calls[0]?.[0][0]).toMatchObject({
      tenantId: null,
      scope: 'AdminQuery',
      key: 'analytics.summary',
      newValue: '{"from":"2026-10-01","groupBy":"week"}',
      actorUserId: A,
      reason: 'Query',
      correlationId: 'req-1',
    });
  });
});
