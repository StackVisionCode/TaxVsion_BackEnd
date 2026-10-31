import { Prisma, type PrismaClient } from '@prisma/client';
import type { MeetingEventLogEntry, MeetingEventScope, MeetingEventType } from '../../domain/meetings/meeting-event-log.js';
import type { MeetingEventLogRepository } from '../../application/ports/meeting-event-log-repository.js';

/**
 * El `seq` se calcula dentro del mismo INSERT con UPDLOCK+HOLDLOCK sobre el rango del scope: dos escritores
 * de la misma reunión se serializan (antes leían el mismo MAX y uno chocaba con el índice único y se perdía,
 * p. ej. un `speaker.stopped`). Otras reuniones no se bloquean. Reintento corto por si SQL Server elige
 * este INSERT como víctima de un deadlock.
 */
const APPEND_RETRIES = 3;
const DEADLOCK_VICTIM = 1205;

export class PrismaMeetingEventLogRepository implements MeetingEventLogRepository {
  constructor(private readonly prisma: PrismaClient) {}

  async append(entry: Omit<MeetingEventLogEntry, 'seq'>): Promise<MeetingEventLogEntry> {
    const payload = entry.payload ? JSON.stringify(entry.payload) : null;
    for (let attempt = 1; ; attempt++) {
      try {
        const rows = await this.prisma.$queryRaw<{ Seq: number }[]>`
          INSERT INTO [dbo].[MeetingEventLog]
            ([Id], [TenantId], [Scope], [ScopeId], [Seq], [At], [Type], [ActorUserId], [TargetUserId], [PayloadJson], [CorrelationId])
          OUTPUT INSERTED.[Seq]
          SELECT ${entry.id}, ${entry.tenantId}, ${entry.scope}, ${entry.scopeId},
                 ISNULL(MAX(l.[Seq]), 0) + 1,
                 ${entry.at}, ${entry.type}, ${nullable(entry.actorUserId)}, ${nullable(entry.targetUserId)}, ${nullable(payload)}, ${nullable(entry.correlationId)}
          FROM [dbo].[MeetingEventLog] l WITH (UPDLOCK, HOLDLOCK)
          WHERE l.[ScopeId] = ${entry.scopeId}`;
        const seq = rows[0]?.Seq;
        if (typeof seq !== 'number') throw new Error('MeetingEventLog insert returned no seq.');
        return { ...entry, seq };
      } catch (err) {
        if (!isRetryable(err) || attempt >= APPEND_RETRIES) throw err;
      }
    }
  }

  async list(input: {
    tenantId: string;
    scope: MeetingEventScope;
    scopeId: string;
    afterSeq?: number;
    types?: readonly string[];
    take: number;
  }): Promise<readonly MeetingEventLogEntry[]> {
    const rows = await this.prisma.meetingEventLog.findMany({
      where: {
        TenantId: input.tenantId,
        Scope: input.scope,
        ScopeId: input.scopeId,
        ...(input.afterSeq !== undefined ? { Seq: { gt: input.afterSeq } } : {}),
        ...(input.types ? { Type: { in: [...input.types] } } : {}),
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

/** Un null como parámetro llega tipado int y SQL Server no lo convierte a uniqueidentifier: va NULL literal. */
function nullable(value: string | null): Prisma.Sql {
  return value === null ? Prisma.sql`NULL` : Prisma.sql`${value}`;
}

/** Choque de índice (no debería pasar ya) o víctima de deadlock: se reintenta. */
function isRetryable(err: unknown): boolean {
  if (err instanceof Prisma.PrismaClientKnownRequestError) {
    if (err.code === 'P2002') return true;
    const meta = err.meta as { code?: string } | undefined;
    if (err.code === 'P2010' && meta?.code === String(DEADLOCK_VICTIM)) return true;
  }
  return err instanceof Error && /deadlock/i.test(err.message);
}
