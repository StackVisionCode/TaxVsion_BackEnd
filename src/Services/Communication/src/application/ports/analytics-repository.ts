/**
 * Snapshot diario por tenant. Contadores acumulados via incrementAsync
 * (SQL Server INCREMENT atomico usando raw update). Es un read model puro.
 */
export interface AnalyticsSnapshotRow {
  readonly tenantId: string;
  readonly day: string; // "YYYY-MM-DD"
  readonly messagesSent: number;
  readonly conversationsStarted: number;
  readonly callsStarted: number;
  readonly callsEnded: number;
  readonly callMinutes: number;
  readonly missedCalls: number;
  readonly meetingsScheduled: number;
  readonly meetingsStarted: number;
  readonly meetingsEnded: number;
  readonly meetingMinutes: number;
  readonly supportTicketsOpened: number;
  readonly supportTicketsResolved: number;
}

export interface AnalyticsRepository {
  incrementCounters(input: {
    tenantId: string;
    day: string;
    increments: Partial<
      Pick<
        AnalyticsSnapshotRow,
        | 'messagesSent'
        | 'conversationsStarted'
        | 'callsStarted'
        | 'callsEnded'
        | 'callMinutes'
        | 'missedCalls'
        | 'meetingsScheduled'
        | 'meetingsStarted'
        | 'meetingsEnded'
        | 'meetingMinutes'
        | 'supportTicketsOpened'
        | 'supportTicketsResolved'
      >
    >;
  }): Promise<void>;

  listForRange(input: {
    tenantId: string;
    fromDay: string;
    toDay: string;
  }): Promise<readonly AnalyticsSnapshotRow[]>;
}
