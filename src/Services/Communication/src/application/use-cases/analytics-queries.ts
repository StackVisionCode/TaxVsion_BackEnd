import type { AnalyticsRepository, AnalyticsSnapshotRow } from '../ports/analytics-repository.js';

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

function defaultRange(): { fromDay: string; toDay: string } {
  const today = new Date();
  const from = new Date(today.getTime() - 29 * 24 * 3600 * 1000);
  return {
    fromDay: from.toISOString().slice(0, 10),
    toDay: today.toISOString().slice(0, 10),
  };
}

export async function analyticsSummary(
  input: { tenantId: string; fromDay?: string; toDay?: string },
  deps: { analytics: AnalyticsRepository },
): Promise<AnalyticsSummary> {
  const range = defaultRange();
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
  deps: { analytics: AnalyticsRepository },
): Promise<AnalyticsTimeline> {
  const range = defaultRange();
  const fromDay = input.fromDay ?? range.fromDay;
  const toDay = input.toDay ?? range.toDay;
  const rows = await deps.analytics.listForRange({ tenantId: input.tenantId, fromDay, toDay });
  return { fromDay, toDay, days: rows };
}
