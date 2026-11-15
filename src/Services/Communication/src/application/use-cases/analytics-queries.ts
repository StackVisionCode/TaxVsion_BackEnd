import type { AnalyticsRepository, AnalyticsSnapshotRow } from '../ports/analytics-repository.js';
import { dayInTimeZone } from '../services/analytics-recorder.js';

export interface AnalyticsQueryDeps {
  readonly analytics: AnalyticsRepository;
  /** Zona IANA de la analítica (`analyticsTimeZone`), la misma que corta los días del snapshot. */
  readonly timeZone: () => Promise<string>;
  readonly now?: () => Date;
}

export interface AnalyticsSummary {
  readonly fromDay: string;
  readonly toDay: string;
  readonly totals: {
    messagesSent: number;
    conversationsStarted: number;
    callsStarted: number;
    callsEnded: number;
    callMinutes: number;
    missedCalls: number;
    meetingsScheduled: number;
    meetingsStarted: number;
    meetingsEnded: number;
    meetingMinutes: number;
    supportTicketsOpened: number;
    supportTicketsResolved: number;
  };
}

export interface AnalyticsTimeline {
  readonly fromDay: string;
  readonly toDay: string;
  readonly days: readonly AnalyticsSnapshotRow[];
}

// TZ03-H3: "hoy" es el día en la zona de analítica, no el UTC.
async function defaultRange(deps: AnalyticsQueryDeps): Promise<{ fromDay: string; toDay: string }> {
  const now = (deps.now ?? (() => new Date()))();
  const toDay = dayInTimeZone(now.toISOString(), await deps.timeZone());
  const fromDay = new Date(Date.parse(`${toDay}T00:00:00Z`) - 29 * 24 * 3600 * 1000).toISOString().slice(0, 10);
  return { fromDay, toDay };
}

export async function analyticsSummary(
  input: { tenantId: string; fromDay?: string; toDay?: string },
  deps: AnalyticsQueryDeps,
): Promise<AnalyticsSummary> {
  const range = await defaultRange(deps);
  const fromDay = input.fromDay ?? range.fromDay;
  const toDay = input.toDay ?? range.toDay;
  const rows = await deps.analytics.listForRange({ tenantId: input.tenantId, fromDay, toDay });
  const totals = {
    messagesSent: 0,
    conversationsStarted: 0,
    callsStarted: 0,
    callsEnded: 0,
    callMinutes: 0,
    missedCalls: 0,
    meetingsScheduled: 0,
    meetingsStarted: 0,
    meetingsEnded: 0,
    meetingMinutes: 0,
    supportTicketsOpened: 0,
    supportTicketsResolved: 0,
  };
  for (const r of rows) {
    totals.messagesSent += r.messagesSent;
    totals.conversationsStarted += r.conversationsStarted;
    totals.callsStarted += r.callsStarted;
    totals.callsEnded += r.callsEnded;
    totals.callMinutes += r.callMinutes;
    totals.missedCalls += r.missedCalls;
    totals.meetingsScheduled += r.meetingsScheduled;
    totals.meetingsStarted += r.meetingsStarted;
    totals.meetingsEnded += r.meetingsEnded;
    totals.meetingMinutes += r.meetingMinutes;
    totals.supportTicketsOpened += r.supportTicketsOpened;
    totals.supportTicketsResolved += r.supportTicketsResolved;
  }
  return { fromDay, toDay, totals };
}

export async function analyticsTimeline(
  input: { tenantId: string; fromDay?: string; toDay?: string },
  deps: AnalyticsQueryDeps,
): Promise<AnalyticsTimeline> {
  const range = await defaultRange(deps);
  const fromDay = input.fromDay ?? range.fromDay;
  const toDay = input.toDay ?? range.toDay;
  const rows = await deps.analytics.listForRange({ tenantId: input.tenantId, fromDay, toDay });
  return { fromDay, toDay, days: rows };
}
