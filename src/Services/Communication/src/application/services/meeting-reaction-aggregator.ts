/**
 * F5 (D-50): las reacciones no se persisten una a una. Este agregado en memoria suma por reunion y
 * minuto UTC; al cambiar de minuto devuelve el minuto cerrado para que el llamador lo registre como
 * `reaction.summary`, y `flush()` cierra lo pendiente cuando la reunion termina. Es por proceso,
 * igual que el agregado de calidad (una reunion SFU vive en el pod que la creo).
 */
export interface ReactionMinuteSummary {
  /** Inicio del minuto en ISO (segundos y milisegundos a cero). */
  readonly minuteUtc: string;
  readonly counts: Readonly<Record<string, number>>;
}

interface OpenMinute {
  readonly minuteUtc: string;
  readonly counts: Map<string, number>;
}

const MINUTE_MS = 60_000;

function minuteOf(at: Date): string {
  return new Date(Math.floor(at.getTime() / MINUTE_MS) * MINUTE_MS).toISOString();
}

function toSummary(open: OpenMinute): ReactionMinuteSummary {
  return { minuteUtc: open.minuteUtc, counts: Object.fromEntries(open.counts) };
}

export class MeetingReactionAggregator {
  private readonly open = new Map<string, OpenMinute>();

  /** Suma una reaccion; si la reunion cambio de minuto devuelve el minuto cerrado (ya retirado). */
  record(meetingId: string, emoji: string, at: Date = new Date()): ReactionMinuteSummary | null {
    const minuteUtc = minuteOf(at);
    let closed: ReactionMinuteSummary | null = null;
    const current = this.open.get(meetingId);
    if (current && current.minuteUtc !== minuteUtc) {
      closed = toSummary(current);
      this.open.delete(meetingId);
    }
    const bucket = this.open.get(meetingId) ?? { minuteUtc, counts: new Map<string, number>() };
    bucket.counts.set(emoji, (bucket.counts.get(emoji) ?? 0) + 1);
    this.open.set(meetingId, bucket);
    return closed;
  }

  /** Cierra y retira lo pendiente de la reunion (fin de reunion); vacio si nunca hubo reacciones. */
  flush(meetingId: string): ReactionMinuteSummary[] {
    const current = this.open.get(meetingId);
    this.open.delete(meetingId);
    return current ? [toSummary(current)] : [];
  }
}
