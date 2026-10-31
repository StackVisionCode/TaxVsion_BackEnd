import { randomUUID } from 'node:crypto';
import { Result, makeError } from '../../domain/shared/result.js';
import {
  ConfigurationAuditReason,
  ConfigurationAuditScope,
} from '../../domain/settings/configuration-audit.js';
import {
  ANALYTICS_COUNTERS,
  ANALYTICS_PEAKS,
  type AnalyticsCounters,
  type AnalyticsPeak,
  type AnalyticsRepository,
  type AnalyticsSnapshotRow,
} from '../ports/analytics-repository.js';
import type { ConfigurationAuditRepository } from '../ports/configuration-audit-repository.js';
import { dayInTimeZone } from '../services/analytics-recorder.js';

/**
 * F10 (ADD-08 T-72): vista de plataforma sobre el snapshot diario (nunca sobre tablas operacionales).
 * Agrupa por día, semana ISO o mes en el servidor; los días ya vienen cortados en la zona de la
 * plataforma, así que semana y mes son aritmética de calendario sobre esas fechas.
 */
export const ANALYTICS_GROUP_BY = ['day', 'week', 'month'] as const;
export type AnalyticsGroupBy = (typeof ANALYTICS_GROUP_BY)[number];

export type AnalyticsPeaks = Record<AnalyticsPeak, number>;

/** Promedios y tasas derivados de los totales (null cuando el denominador es 0). */
export interface AnalyticsDerived {
  readonly averageCallSeconds: number | null;
  readonly callFailureRate: number | null;
  readonly averageMeetingSeconds: number | null;
  readonly averageParticipantsPerMeeting: number | null;
  readonly guestShare: number | null;
  readonly recordingFailureRate: number | null;
  readonly transcriptFailureRate: number | null;
}

export interface AnalyticsPeriod {
  /** Primer día del periodo ('YYYY-MM-DD'). */
  readonly period: string;
  /** '2026-10-31', '2026-W44' o '2026-10'. */
  readonly label: string;
  readonly activeTenants: number;
  readonly totals: AnalyticsCounters;
  /** Máximo de una oficina en un día del periodo (los picos no se suman entre oficinas). */
  readonly peaks: AnalyticsPeaks;
}

export interface PlatformAnalyticsSummary {
  readonly fromDay: string;
  readonly toDay: string;
  readonly groupBy: AnalyticsGroupBy;
  readonly timeZone: string;
  readonly filters: { readonly tenantId: string | null; readonly planCode: string | null };
  readonly activeTenants: number;
  readonly totals: AnalyticsCounters;
  readonly peaks: AnalyticsPeaks;
  readonly derived: AnalyticsDerived;
  readonly periods: readonly AnalyticsPeriod[];
}

export interface PlatformAnalyticsTenantRow {
  readonly tenantId: string;
  /** Plan del último día con actividad en el rango. */
  readonly planCode: string | null;
  readonly activeDays: number;
  readonly totals: AnalyticsCounters;
  readonly peaks: AnalyticsPeaks;
  readonly derived: AnalyticsDerived;
}

export interface PlatformAnalyticsTenants {
  readonly fromDay: string;
  readonly toDay: string;
  readonly timeZone: string;
  readonly planCode: string | null;
  readonly total: number;
  readonly tenants: readonly PlatformAnalyticsTenantRow[];
}

export interface PlatformAnalyticsRangeInput {
  readonly fromDay?: string;
  readonly toDay?: string;
  readonly planCode?: string;
}

export interface PlatformAnalyticsDeps {
  readonly analytics: Pick<AnalyticsRepository, 'listAllForRange'>;
  readonly timeZone: () => Promise<string>;
  /** Ventana por defecto y tope del rango (configuración, no constantes de negocio). */
  readonly defaultRangeDays: number;
  readonly maxRangeDays: number;
  readonly now?: () => Date;
}

const DAY_MS = 86_400_000;
const DAY_PATTERN = /^\d{4}-\d{2}-\d{2}$/;

export async function platformAnalyticsSummary(
  input: PlatformAnalyticsRangeInput & { groupBy?: AnalyticsGroupBy; tenantId?: string },
  deps: PlatformAnalyticsDeps,
): Promise<Result<PlatformAnalyticsSummary>> {
  const range = await resolveRange(input, deps);
  if (!range.isSuccess) return Result.fail(range.error);
  const { fromDay, toDay, timeZone } = range.value;
  const groupBy = input.groupBy ?? 'day';
  const rows = await deps.analytics.listAllForRange({
    fromDay,
    toDay,
    ...(input.tenantId ? { tenantId: input.tenantId } : {}),
    ...(input.planCode ? { planCode: input.planCode } : {}),
  });

  const buckets = new Map<string, { label: string; rows: AnalyticsSnapshotRow[] }>();
  for (const row of rows) {
    const { period, label } = bucketOf(row.day, groupBy);
    const bucket = buckets.get(period) ?? { label, rows: [] };
    bucket.rows.push(row);
    buckets.set(period, bucket);
  }
  const periods = [...buckets.entries()]
    .sort(([a], [b]) => a.localeCompare(b))
    .map(([period, bucket]) => ({
      period,
      label: bucket.label,
      activeTenants: new Set(bucket.rows.map((r) => r.tenantId)).size,
      totals: sumCounters(bucket.rows),
      peaks: maxPeaks(bucket.rows),
    }));
  const totals = sumCounters(rows);
  return Result.ok({
    fromDay,
    toDay,
    groupBy,
    timeZone,
    filters: { tenantId: input.tenantId ?? null, planCode: input.planCode ?? null },
    activeTenants: new Set(rows.map((r) => r.tenantId)).size,
    totals,
    peaks: maxPeaks(rows),
    derived: derive(totals),
    periods,
  });
}

export async function platformAnalyticsTenants(
  input: PlatformAnalyticsRangeInput & { limit: number },
  deps: PlatformAnalyticsDeps,
): Promise<Result<PlatformAnalyticsTenants>> {
  const range = await resolveRange(input, deps);
  if (!range.isSuccess) return Result.fail(range.error);
  const { fromDay, toDay, timeZone } = range.value;
  const rows = await deps.analytics.listAllForRange({ fromDay, toDay, ...(input.planCode ? { planCode: input.planCode } : {}) });

  const byTenant = new Map<string, AnalyticsSnapshotRow[]>();
  for (const row of rows) {
    const list = byTenant.get(row.tenantId) ?? [];
    list.push(row);
    byTenant.set(row.tenantId, list);
  }
  const tenants = [...byTenant.entries()].map(([tenantId, list]) => {
    const totals = sumCounters(list);
    const latestWithPlan = [...list].reverse().find((r) => r.planCode !== null);
    return {
      tenantId,
      planCode: latestWithPlan?.planCode ?? null,
      activeDays: list.length,
      totals,
      peaks: maxPeaks(list),
      derived: derive(totals),
    };
  });
  // Más uso real primero: segundos en llamadas y reuniones, luego mensajes.
  tenants.sort(
    (a, b) =>
      b.totals.callSeconds + b.totals.meetingSeconds - (a.totals.callSeconds + a.totals.meetingSeconds) ||
      b.totals.messagesSent - a.totals.messagesSent ||
      a.tenantId.localeCompare(b.tenantId),
  );
  return Result.ok({
    fromDay,
    toDay,
    timeZone,
    planCode: input.planCode ?? null,
    total: tenants.length,
    tenants: tenants.slice(0, input.limit),
  });
}

/** C.3: cada consulta de plataforma queda en `ConfigurationAudit` (actor, endpoint y filtros, sin datos). */
export async function auditPlatformAnalyticsQuery(
  input: { endpoint: string; actorUserId: string; params: Record<string, unknown>; correlationId?: string | null },
  deps: { audit: Pick<ConfigurationAuditRepository, 'append'> },
): Promise<void> {
  const params = Object.fromEntries(Object.entries(input.params).filter(([, v]) => v !== undefined && v !== null && v !== ''));
  await deps.audit.append([
    {
      id: randomUUID(),
      tenantId: null,
      scope: ConfigurationAuditScope.AdminQuery,
      key: `analytics.${input.endpoint}`,
      oldValue: null,
      newValue: JSON.stringify(params).slice(0, 512),
      actorUserId: input.actorUserId,
      reason: ConfigurationAuditReason.Query,
      correlationId: input.correlationId ?? null,
      createdAtUtc: new Date(),
    },
  ]);
}

async function resolveRange(
  input: PlatformAnalyticsRangeInput,
  deps: PlatformAnalyticsDeps,
): Promise<Result<{ fromDay: string; toDay: string; timeZone: string }>> {
  const timeZone = await deps.timeZone().catch(() => 'UTC');
  const now = (deps.now ?? (() => new Date()))();
  const today = dayInTimeZone(now.toISOString(), timeZone);
  const toDay = input.toDay ?? today;
  const fromDay = input.fromDay ?? addDays(toDay, -(deps.defaultRangeDays - 1));
  if (!isDay(fromDay) || !isDay(toDay)) {
    return Result.fail(makeError('Analytics.InvalidRange', 'from and to must be dates in YYYY-MM-DD format.'));
  }
  if (fromDay > toDay) return Result.fail(makeError('Analytics.InvalidRange', 'from must be on or before to.'));
  const days = Math.round((Date.parse(`${toDay}T00:00:00Z`) - Date.parse(`${fromDay}T00:00:00Z`)) / DAY_MS) + 1;
  if (days > deps.maxRangeDays) {
    return Result.fail(makeError('Analytics.RangeTooLarge', `The range can cover at most ${deps.maxRangeDays} days.`));
  }
  return Result.ok({ fromDay, toDay, timeZone });
}

function isDay(value: string): boolean {
  return DAY_PATTERN.test(value) && !Number.isNaN(Date.parse(`${value}T00:00:00Z`)) && new Date(`${value}T00:00:00Z`).toISOString().startsWith(value);
}

function addDays(day: string, delta: number): string {
  return new Date(Date.parse(`${day}T00:00:00Z`) + delta * DAY_MS).toISOString().slice(0, 10);
}

/** Periodo al que pertenece un día: él mismo, el lunes de su semana ISO o el día 1 de su mes. */
export function bucketOf(day: string, groupBy: AnalyticsGroupBy): { period: string; label: string } {
  if (groupBy === 'day') return { period: day, label: day };
  if (groupBy === 'month') return { period: `${day.slice(0, 7)}-01`, label: day.slice(0, 7) };
  const date = new Date(`${day}T00:00:00Z`);
  const isoWeekday = date.getUTCDay() === 0 ? 7 : date.getUTCDay();
  const monday = addDays(day, 1 - isoWeekday);
  // El jueves de la semana decide el año ISO y el número de semana.
  const thursday = new Date(Date.parse(`${monday}T00:00:00Z`) + 3 * DAY_MS);
  const isoYear = thursday.getUTCFullYear();
  const firstThursday = new Date(Date.UTC(isoYear, 0, 4));
  const firstMonday = firstThursday.getTime() - ((firstThursday.getUTCDay() === 0 ? 7 : firstThursday.getUTCDay()) - 1) * DAY_MS;
  const week = Math.round((Date.parse(`${monday}T00:00:00Z`) - firstMonday) / (7 * DAY_MS)) + 1;
  return { period: monday, label: `${isoYear}-W${String(week).padStart(2, '0')}` };
}

function emptyCounters(): AnalyticsCounters {
  return Object.fromEntries(ANALYTICS_COUNTERS.map((key) => [key, 0])) as AnalyticsCounters;
}

function sumCounters(rows: readonly AnalyticsSnapshotRow[]): AnalyticsCounters {
  const totals = emptyCounters();
  for (const row of rows) for (const key of ANALYTICS_COUNTERS) totals[key] += row[key];
  return totals;
}

function maxPeaks(rows: readonly AnalyticsSnapshotRow[]): AnalyticsPeaks {
  const peaks = Object.fromEntries(ANALYTICS_PEAKS.map((key) => [key, 0])) as AnalyticsPeaks;
  for (const row of rows) for (const key of ANALYTICS_PEAKS) peaks[key] = Math.max(peaks[key], row[key]);
  return peaks;
}

function ratio(numerator: number, denominator: number, digits = 4): number | null {
  if (denominator <= 0) return null;
  const factor = 10 ** digits;
  return Math.round((numerator / denominator) * factor) / factor;
}

function derive(t: AnalyticsCounters): AnalyticsDerived {
  const recordingOutcomes = t.recordingsReady + t.recordingsFailed;
  const transcriptOutcomes = t.transcriptsReady + t.transcriptsFailed;
  return {
    averageCallSeconds: ratio(t.callSeconds, t.callsCompleted, 1),
    callFailureRate: ratio(t.callsFailed, t.callsEnded),
    averageMeetingSeconds: ratio(t.meetingSeconds, t.meetingsEnded, 1),
    averageParticipantsPerMeeting: ratio(t.participantsJoined, t.meetingsStarted, 2),
    guestShare: ratio(t.guestsJoined, t.participantsJoined),
    recordingFailureRate: ratio(t.recordingsFailed, recordingOutcomes),
    transcriptFailureRate: ratio(t.transcriptsFailed, transcriptOutcomes),
  };
}
