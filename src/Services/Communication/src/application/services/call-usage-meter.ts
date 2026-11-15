import { type CallUsageMeter, type CallUsageRepository, yearMonthUtc } from '../ports/call-usage-repository.js';
import type { LimitsRepository } from '../ports/settings-repository.js';
import type { TenantTimeZoneStore } from '../ports/tenant-time-zone-store.js';
import { localDate } from './office-clock.js';

/**
 * P11: el mes del cupo empieza el día de alta de la suscripción (ancla) a medianoche de la oficina; si el mes es más
 * corto, el último día. La clave es el `YYYY-MM` local en que empezó el período. Sin ancla, el mes UTC de antes.
 */
export function usageMonthKey(at: Date, anchorUtc: Date | null, timeZone: string | null): string {
  if (!anchorUtc) return yearMonthUtc(at);
  const zone = timeZone ?? 'UTC';
  const anchorDay = Number(localDate(anchorUtc, zone).slice(8, 10));
  const [year, month, day] = localDate(at, zone).split('-').map(Number) as [number, number, number];
  if (day >= Math.min(anchorDay, daysInMonth(year, month))) return monthKey(year, month);
  return month === 1 ? monthKey(year - 1, 12) : monthKey(year, month - 1);
}

function daysInMonth(year: number, month: number): number {
  return new Date(Date.UTC(year, month, 0)).getUTCDate();
}

function monthKey(year: number, month: number): string {
  return `${year}-${String(month).padStart(2, '0')}`;
}

/** Cuenta los minutos de llamada en el período de la oficina: ancla de los límites del plan y zona proyectada. */
export class PeriodCallUsageMeter implements CallUsageMeter {
  constructor(
    private readonly deps: {
      readonly usage: CallUsageRepository;
      readonly limits: Pick<LimitsRepository, 'findByTenantId'>;
      readonly timeZones: Pick<TenantTimeZoneStore, 'find'>;
    },
  ) {}

  async addSeconds(input: { tenantId: string; at: Date; seconds: number }): Promise<void> {
    const yearMonth = await this.periodKey(input.tenantId, input.at);
    await this.deps.usage.addSeconds({ tenantId: input.tenantId, yearMonth, seconds: input.seconds });
  }

  async secondsThisPeriod(input: { tenantId: string; at: Date }): Promise<number> {
    return this.deps.usage.getSeconds({ tenantId: input.tenantId, yearMonth: await this.periodKey(input.tenantId, input.at) });
  }

  private async periodKey(tenantId: string, at: Date): Promise<string> {
    const [limits, zone] = await Promise.all([this.deps.limits.findByTenantId(tenantId), this.deps.timeZones.find(tenantId)]);
    return usageMonthKey(at, limits?.usagePeriodAnchorUtc ?? null, zone?.timeZoneId ?? null);
  }
}
