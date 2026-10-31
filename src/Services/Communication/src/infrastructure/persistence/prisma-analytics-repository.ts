import { Prisma, type PrismaClient } from '@prisma/client';
import {
  ANALYTICS_COUNTERS,
  ANALYTICS_PEAKS,
  type AnalyticsCounters,
  type AnalyticsPeak,
  type AnalyticsRepository,
  type AnalyticsSnapshotRow,
} from '../../application/ports/analytics-repository.js';

type SnapshotRecord = Prisma.CommunicationAnalyticsSnapshotGetPayload<Record<string, never>>;

/** camelCase del puerto → columna PascalCase de la tabla. */
function columnOf(key: string): string {
  return key.charAt(0).toUpperCase() + key.slice(1);
}

function dayDate(day: string): Date {
  return new Date(`${day}T00:00:00.000Z`);
}

/**
 * Upsert-then-increment. En SQL Server no hay atomic UPSERT + INCR trivial; se crea la fila con
 * ceros y luego cada columna se incrementa de forma atomica. Dos consumidores creando el mismo dia
 * a la vez chocan en la PK: el perdedor sigue con el update.
 */
export class PrismaAnalyticsRepository implements AnalyticsRepository {
  constructor(private readonly prisma: PrismaClient) {}

  async incrementCounters(input: {
    tenantId: string;
    day: string;
    increments: Partial<AnalyticsCounters>;
    planCode?: string | null;
  }): Promise<void> {
    const data: Record<string, unknown> = {};
    for (const [key, value] of Object.entries(input.increments)) {
      if (typeof value !== 'number' || !Number.isFinite(value) || value === 0) continue;
      const amount = Math.round(value);
      data[columnOf(key)] = { increment: key === 'recordingBytes' ? BigInt(amount) : amount };
    }
    if (Object.keys(data).length === 0) return;
    if (input.planCode) data['PlanCode'] = input.planCode;
    await this.ensureRow(input.tenantId, input.day, input.planCode ?? null);
    await this.prisma.communicationAnalyticsSnapshot.update({
      where: { TenantId_Day: { TenantId: input.tenantId, Day: dayDate(input.day) } },
      data: data as Prisma.CommunicationAnalyticsSnapshotUncheckedUpdateInput,
    });
  }

  async raisePeaks(input: { tenantId: string; day: string; peaks: Partial<Record<AnalyticsPeak, number>> }): Promise<void> {
    const meetings = Math.max(0, Math.round(input.peaks.peakConcurrentMeetings ?? 0));
    const participants = Math.max(0, Math.round(input.peaks.peakConcurrentParticipants ?? 0));
    if (meetings === 0 && participants === 0) return;
    await this.ensureRow(input.tenantId, input.day, null);
    // Un solo UPDATE condicional: dos eventos concurrentes nunca bajan el maximo.
    await this.prisma.$executeRaw`
      UPDATE [dbo].[CommunicationAnalyticsSnapshot]
      SET [PeakConcurrentMeetings] = CASE WHEN [PeakConcurrentMeetings] < ${meetings} THEN ${meetings} ELSE [PeakConcurrentMeetings] END,
          [PeakConcurrentParticipants] = CASE WHEN [PeakConcurrentParticipants] < ${participants} THEN ${participants} ELSE [PeakConcurrentParticipants] END,
          [UpdatedAtUtc] = SYSUTCDATETIME()
      WHERE [TenantId] = CAST(${input.tenantId} AS UNIQUEIDENTIFIER) AND [Day] = CAST(${input.day} AS DATE)`;
  }

  async listForRange(input: { tenantId: string; fromDay: string; toDay: string }): Promise<readonly AnalyticsSnapshotRow[]> {
    const rows = await this.prisma.communicationAnalyticsSnapshot.findMany({
      where: { TenantId: input.tenantId, Day: { gte: dayDate(input.fromDay), lte: dayDate(input.toDay) } },
      orderBy: { Day: 'asc' },
    });
    return rows.map(toRow);
  }

  async listAllForRange(input: {
    fromDay: string;
    toDay: string;
    tenantId?: string;
    planCode?: string;
  }): Promise<readonly AnalyticsSnapshotRow[]> {
    const rows = await this.prisma.communicationAnalyticsSnapshot.findMany({
      where: {
        Day: { gte: dayDate(input.fromDay), lte: dayDate(input.toDay) },
        ...(input.tenantId ? { TenantId: input.tenantId } : {}),
        ...(input.planCode ? { PlanCode: input.planCode } : {}),
      },
      orderBy: [{ Day: 'asc' }, { TenantId: 'asc' }],
    });
    return rows.map(toRow);
  }

  async purgeBefore(beforeDay: string): Promise<number> {
    const result = await this.prisma.communicationAnalyticsSnapshot.deleteMany({ where: { Day: { lt: dayDate(beforeDay) } } });
    return result.count;
  }

  private async ensureRow(tenantId: string, day: string, planCode: string | null): Promise<void> {
    try {
      await this.prisma.communicationAnalyticsSnapshot.upsert({
        where: { TenantId_Day: { TenantId: tenantId, Day: dayDate(day) } },
        create: { TenantId: tenantId, Day: dayDate(day), PlanCode: planCode },
        update: {},
      });
    } catch (err) {
      if (!(err instanceof Prisma.PrismaClientKnownRequestError && err.code === 'P2002')) throw err;
    }
  }
}

function toRow(r: SnapshotRecord): AnalyticsSnapshotRow {
  const record = r as unknown as Record<string, unknown>;
  const values: Record<string, number> = {};
  for (const key of [...ANALYTICS_COUNTERS, ...ANALYTICS_PEAKS]) {
    const value = record[columnOf(key)];
    values[key] = typeof value === 'bigint' ? Number(value) : typeof value === 'number' ? value : 0;
  }
  return {
    ...(values as Omit<AnalyticsSnapshotRow, 'tenantId' | 'day' | 'planCode'>),
    tenantId: r.TenantId,
    day: r.Day.toISOString().slice(0, 10),
    planCode: r.PlanCode,
  };
}
