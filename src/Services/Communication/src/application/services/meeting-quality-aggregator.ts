/**
 * Agregado en memoria por reunion (INV-03 §13.6 → ADD-08): niveles reportados por los participantes,
 * reportes via TURN, scores del SFU y fallos de transporte. Se vacia al publicar el resumen al terminar.
 * Es por proceso: una reunion SFU vive en el pod que la creo (misma limitacion documentada del SFU).
 */
export interface MeetingQualitySummary {
  readonly participants: number;
  readonly levels: Record<string, number>;
  readonly relayReports: number;
  readonly sfuScoreSamples: number;
  readonly sfuScoreAvg: number | null;
  readonly transportFailures: number;
}

interface Bucket {
  readonly users: Set<string>;
  readonly levels: Map<string, number>;
  relayReports: number;
  scoreSamples: number;
  scoreSum: number;
  transportFailures: number;
}

export class MeetingQualityAggregator {
  private readonly buckets = new Map<string, Bucket>();

  recordLevel(meetingId: string, userId: string, level: string, viaRelay: boolean | undefined): void {
    const b = this.bucket(meetingId);
    b.users.add(userId);
    b.levels.set(level, (b.levels.get(level) ?? 0) + 1);
    if (viaRelay) b.relayReports += 1;
  }

  recordScore(meetingId: string, userId: string, score: number): void {
    const b = this.bucket(meetingId);
    b.users.add(userId);
    b.scoreSamples += 1;
    b.scoreSum += score;
  }

  recordTransportFailure(meetingId: string, userId: string): void {
    const b = this.bucket(meetingId);
    b.users.add(userId);
    b.transportFailures += 1;
  }

  /** Resumen y limpieza: null si la reunion nunca reporto nada (p. ej. mesh sin medidas). */
  summarize(meetingId: string): MeetingQualitySummary | null {
    const b = this.buckets.get(meetingId);
    this.buckets.delete(meetingId);
    if (!b) return null;
    return {
      participants: b.users.size,
      levels: Object.fromEntries(b.levels),
      relayReports: b.relayReports,
      sfuScoreSamples: b.scoreSamples,
      sfuScoreAvg: b.scoreSamples > 0 ? Math.round((b.scoreSum / b.scoreSamples) * 100) / 100 : null,
      transportFailures: b.transportFailures,
    };
  }

  private bucket(meetingId: string): Bucket {
    let b = this.buckets.get(meetingId);
    if (!b) {
      b = { users: new Set(), levels: new Map(), relayReports: 0, scoreSamples: 0, scoreSum: 0, transportFailures: 0 };
      this.buckets.set(meetingId, b);
    }
    return b;
  }
}
