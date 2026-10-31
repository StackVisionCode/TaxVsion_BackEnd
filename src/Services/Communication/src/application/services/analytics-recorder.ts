import type { AnalyticsCounters, AnalyticsRepository } from '../ports/analytics-repository.js';
import type { LiveConcurrencyReader } from '../ports/live-concurrency-reader.js';

export interface AnalyticsRecorderDeps {
  readonly analytics: AnalyticsRepository;
  /** Zona IANA de la plataforma (`analyticsTimeZone`): corta los días del snapshot. */
  readonly timeZone: () => Promise<string>;
  /** Plan vigente de la oficina (proyección de Subscription ya consumida), para "uso por plan". */
  readonly planCodeOf?: (tenantId: string) => Promise<string | null>;
  readonly live?: LiveConcurrencyReader;
}

/** 'YYYY-MM-DD' del instante en la zona dada (en-CA formatea año-mes-día). */
export function dayInTimeZone(iso: string, timeZone: string): string {
  const instant = new Date(iso);
  if (Number.isNaN(instant.getTime())) return iso.slice(0, 10);
  try {
    return new Intl.DateTimeFormat('en-CA', { timeZone, year: 'numeric', month: '2-digit', day: '2-digit' }).format(instant);
  } catch {
    return instant.toISOString().slice(0, 10);
  }
}

/**
 * F10 (ADD-08 T-71): un solo punto para registrar en el snapshot. Resuelve el día en la zona de la
 * plataforma y el plan de la oficina; si la zona o el plan no se pueden leer, cae a UTC / sin plan
 * (un contador nunca se pierde por eso).
 */
export class AnalyticsRecorder {
  constructor(private readonly deps: AnalyticsRecorderDeps) {}

  async day(occurredOnUtc: string): Promise<string> {
    const timeZone = await this.deps.timeZone().catch(() => 'UTC');
    return dayInTimeZone(occurredOnUtc, timeZone);
  }

  async count(tenantId: string, occurredOnUtc: string, increments: Partial<AnalyticsCounters>): Promise<void> {
    const [day, planCode] = await Promise.all([
      this.day(occurredOnUtc),
      this.deps.planCodeOf ? this.deps.planCodeOf(tenantId).catch(() => null) : Promise.resolve(null),
    ]);
    await this.deps.analytics.incrementCounters({ tenantId, day, increments, planCode });
  }

  /** Tras una entrada (reunión iniciada, participante dentro) sube los máximos del día si toca. */
  async observePeaks(tenantId: string, occurredOnUtc: string): Promise<void> {
    if (!this.deps.live) return;
    const [day, now] = await Promise.all([this.day(occurredOnUtc), this.deps.live.countForTenant(tenantId)]);
    await this.deps.analytics.raisePeaks({
      tenantId,
      day,
      peaks: { peakConcurrentMeetings: now.meetings, peakConcurrentParticipants: now.participants },
    });
  }
}
