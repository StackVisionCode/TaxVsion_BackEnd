import { Prisma, type PrismaClient } from '@prisma/client';
import type { MeetingEventLogEntry, MeetingEventScope, MeetingEventType } from '../../domain/meetings/meeting-event-log.js';
import type { MeetingEventLogRepository } from '../../application/ports/meeting-event-log-repository.js';

/** Dos escritores del mismo scope pueden calcular el mismo `seq`; el índice único lo detecta y se reintenta. */
const SEQ_RETRIES = 3;

export class PrismaMeetingEventLogRepository implements MeetingEventLogRepository {
  constructor(private readonly prisma: PrismaClient) {}

  async append(entry: Omit<MeetingEventLogEntry, 'seq'>): Promise<MeetingEventLogEntry> {
    for (let attempt = 1; ; attempt++) {
      const last = await this.prisma.meetingEventLog.findFirst({
        where: { ScopeId: entry.scopeId },
        orderBy: { Seq: 'desc' },
        select: { Seq: true },
      });
      const seq = (last?.Seq ?? 0) + 1;
      try {
        await this.prisma.meetingEventLog.create({
          data: {
            Id: entry.id,
            TenantId: entry.tenantId,
            Scope: entry.scope,
            ScopeId: entry.scopeId,
            Seq: seq,
            At: entry.at,
            Type: entry.type,
            ActorUserId: entry.actorUserId,
            TargetUserId: entry.targetUserId,
            PayloadJson: entry.payload ? JSON.stringify(entry.payload) : null,
            CorrelationId: entry.correlationId,
          },
        });
        return { ...entry, seq };
      } catch (err) {
        const duplicateSeq = err instanceof Prisma.PrismaClientKnownRequestError && err.code === 'P2002';
        if (!duplicateSeq || attempt >= SEQ_RETRIES) throw err;
      }
    }
  }

  async list(input: {
    tenantId: string;
    scope: MeetingEventScope;
    scopeId: string;
    afterSeq?: number;
    take: number;
  }): Promise<readonly MeetingEventLogEntry[]> {
    const rows = await this.prisma.meetingEventLog.findMany({
      where: {
        TenantId: input.tenantId,
        Scope: input.scope,
        ScopeId: input.scopeId,
        ...(input.afterSeq !== undefined ? { Seq: { gt: input.afterSeq } } : {}),
      },
      orderBy: { Seq: 'asc' },
      take: input.take,
    });
    return rows.map((r) => ({
      id: r.Id,
      tenantId: r.TenantId,
      scope: r.Scope as MeetingEventScope,
      scopeId: r.ScopeId,
      seq: r.Seq,
      at: r.At,
      type: r.Type as MeetingEventType,
      actorUserId: r.ActorUserId,
      targetUserId: r.TargetUserId,
      payload: r.PayloadJson ? (JSON.parse(r.PayloadJson) as Record<string, unknown>) : null,
      correlationId: r.CorrelationId,
    }));
  }
}
