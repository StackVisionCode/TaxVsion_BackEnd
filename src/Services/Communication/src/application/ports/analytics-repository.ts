/**
 * Snapshot diario por tenant. Contadores acumulados via incrementCounters (upsert + increment
 * atomico por columna). Es un read model puro: nunca se recalcula escaneando tablas operacionales.
 */

/** Contadores que se suman. Los de la vista de oficina (Fase 7) y los de plataforma (F10, ADD-08 T-71). */
export const ANALYTICS_COUNTERS = [
  'messagesSent',
  'conversationsStarted',
  'callsStarted',
  'callsEnded',
  'callMinutes',
  'missedCalls',
  'meetingsScheduled',
  'meetingsStarted',
  'meetingsEnded',
  'meetingMinutes',
  'supportTicketsOpened',
  'supportTicketsResolved',
  'audioCallsStarted',
  'videoCallsStarted',
  'callsUpgradedToVideo',
  'callsCompleted',
  'callsFailed',
  'callsRejected',
  'callsCancelled',
  'callsEndedByLimit',
  'callSeconds',
  'meetingsCancelled',
  'meetingsExpired',
  'meetingsEndedBySystem',
  'meetingsMesh',
  'meetingsSfu',
  'meetingSeconds',
  'participantsJoined',
  'guestsJoined',
  'participantSeconds',
  'rejoins',
  'participantsDroppedAfterGrace',
  'transportFailures',
  'qualitySummaries',
  'qualityExcellentReports',
  'qualityGoodReports',
  'qualityFairReports',
  'qualityPoorReports',
  'qualityDisconnectedReports',
  'relayReports',
  'recordingsStarted',
  'recordingsReady',
  'recordingsFailed',
  'recordingSeconds',
  'recordingBytes',
  'transcriptsReady',
  'transcriptsFailed',
  'transcriptAudioSeconds',
  'transcriptProcessingSeconds',
  'invitationsCreated',
  'invitationsUsed',
  'invitationsExpired',
  'invitationsRevoked',
] as const;

export type AnalyticsCounter = (typeof ANALYTICS_COUNTERS)[number];

/** Máximos del día (no se suman entre eventos; entre días/oficinas se toma el mayor). */
export const ANALYTICS_PEAKS = ['peakConcurrentMeetings', 'peakConcurrentParticipants'] as const;
export type AnalyticsPeak = (typeof ANALYTICS_PEAKS)[number];

export type AnalyticsCounters = Record<AnalyticsCounter, number>;

export interface AnalyticsSnapshotRow extends AnalyticsCounters, Record<AnalyticsPeak, number> {
  readonly tenantId: string;
  readonly day: string; // "YYYY-MM-DD"
  readonly planCode: string | null;
}

export interface AnalyticsRepository {
  incrementCounters(input: {
    tenantId: string;
    day: string;
    increments: Partial<AnalyticsCounters>;
    /** Plan de la oficina al registrar (el último del día queda). */
    planCode?: string | null;
  }): Promise<void>;

  /** Sube los máximos del día si lo observado ahora es mayor. */
  raisePeaks(input: { tenantId: string; day: string; peaks: Partial<Record<AnalyticsPeak, number>> }): Promise<void>;

  listForRange(input: {
    tenantId: string;
    fromDay: string;
    toDay: string;
  }): Promise<readonly AnalyticsSnapshotRow[]>;

  /** Vista de plataforma: todas las oficinas (o una) del rango, opcionalmente de un plan. */
  listAllForRange(input: {
    fromDay: string;
    toDay: string;
    tenantId?: string;
    planCode?: string;
  }): Promise<readonly AnalyticsSnapshotRow[]>;

  /** Retención (T-73): borra los días anteriores a `beforeDay`. Devuelve cuántas filas. */
  purgeBefore(beforeDay: string): Promise<number>;
}
