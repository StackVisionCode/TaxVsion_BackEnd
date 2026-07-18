import { Prisma, type PrismaClient } from '@prisma/client';
import type {
  AnalyticsRepository,
  AnalyticsSnapshotRow,
} from '../../application/ports/analytics-repository.js';

/**
 * Upsert-then-increment. En SQL Server no hay atomic UPSERT + INCR trivial;
 * usamos upsert() para crear con ceros y luego update con increments. La
 * carga esperada (varios eventos por segundo por tenant a lo sumo) hace que
 * la solucion sea suficiente sin locks explicitos.
 */
export class PrismaAnalyticsRepository implements AnalyticsRepository {
  constructor(private readonly prisma: PrismaClient) {}

  async incrementCounters(input: {
    tenantId: string;
    day: string;
    increments: Partial<Record<string, number>>;
  }): Promise<void> {
    const dayDate = new Date(`${input.day}T00:00:00.000Z`);
    await this.prisma.communicationAnalyticsSnapshot.upsert({
      where: { TenantId_Day: { TenantId: input.tenantId, Day: dayDate } },
      create: { TenantId: input.tenantId, Day: dayDate },
      update: {},
    });
    const data: Prisma.CommunicationAnalyticsSnapshotUncheckedUpdateInput = {};
    const inc = input.increments;
    if (inc['messagesSent']) data.MessagesSent = { increment: inc['messagesSent'] };
    if (inc['conversationsStarted']) data.ConversationsStarted = { increment: inc['conversationsStarted'] };
    if (inc['callsStarted']) data.CallsStarted = { increment: inc['callsStarted'] };
    if (inc['callsEnded']) data.CallsEnded = { increment: inc['callsEnded'] };
    if (inc['callMinutes']) data.CallMinutes = { increment: inc['callMinutes'] };
    if (inc['missedCalls']) data.MissedCalls = { increment: inc['missedCalls'] };
    if (inc['meetingsScheduled']) data.MeetingsScheduled = { increment: inc['meetingsScheduled'] };
    if (inc['meetingsStarted']) data.MeetingsStarted = { increment: inc['meetingsStarted'] };
    if (inc['meetingsEnded']) data.MeetingsEnded = { increment: inc['meetingsEnded'] };
    if (inc['meetingMinutes']) data.MeetingMinutes = { increment: inc['meetingMinutes'] };
    if (inc['supportTicketsOpened']) data.SupportTicketsOpened = { increment: inc['supportTicketsOpened'] };
    if (inc['supportTicketsResolved']) data.SupportTicketsResolved = { increment: inc['supportTicketsResolved'] };
    if (Object.keys(data).length === 0) return;
    await this.prisma.communicationAnalyticsSnapshot.update({
      where: { TenantId_Day: { TenantId: input.tenantId, Day: dayDate } },
      data,
    });
  }

  async listForRange(input: {
    tenantId: string;
    fromDay: string;
    toDay: string;
  }): Promise<readonly AnalyticsSnapshotRow[]> {
    const from = new Date(`${input.fromDay}T00:00:00.000Z`);
    const to = new Date(`${input.toDay}T00:00:00.000Z`);
    const rows = await this.prisma.communicationAnalyticsSnapshot.findMany({
      where: { TenantId: input.tenantId, Day: { gte: from, lte: to } },
      orderBy: { Day: 'asc' },
    });
    return rows.map((r) => ({
      tenantId: r.TenantId,
      day: r.Day.toISOString().slice(0, 10),
      messagesSent: r.MessagesSent,
      conversationsStarted: r.ConversationsStarted,
      callsStarted: r.CallsStarted,
      callsEnded: r.CallsEnded,
      callMinutes: r.CallMinutes,
      missedCalls: r.MissedCalls,
      meetingsScheduled: r.MeetingsScheduled,
      meetingsStarted: r.MeetingsStarted,
      meetingsEnded: r.MeetingsEnded,
      meetingMinutes: r.MeetingMinutes,
      supportTicketsOpened: r.SupportTicketsOpened,
      supportTicketsResolved: r.SupportTicketsResolved,
    }));
  }
}
