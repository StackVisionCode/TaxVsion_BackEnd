import type { PrismaClient } from '@prisma/client';
import { MeetingStatus, ParticipantStatus } from '../../domain/meetings/meeting-enums.js';
import type { LiveConcurrency, LiveConcurrencyReader } from '../../application/ports/live-concurrency-reader.js';

/** Conteos por índice (`TenantId, Status`): solo números, nunca filas ni nombres. */
export class PrismaLiveConcurrencyReader implements LiveConcurrencyReader {
  constructor(private readonly prisma: PrismaClient) {}

  async countForTenant(tenantId: string): Promise<LiveConcurrency> {
    const [meetings, participants] = await Promise.all([
      this.prisma.meeting.count({ where: { TenantId: tenantId, Status: MeetingStatus.Live } }),
      this.prisma.meetingParticipant.count({
        where: { TenantId: tenantId, Status: ParticipantStatus.Joined, Meeting: { Status: MeetingStatus.Live } },
      }),
    ]);
    return { meetings, participants };
  }

  async countPlatform(): Promise<LiveConcurrency & { byTenant: Array<LiveConcurrency & { tenantId: string }> }> {
    const [meetings, participants] = await Promise.all([
      this.prisma.meeting.groupBy({ by: ['TenantId'], where: { Status: MeetingStatus.Live }, _count: { _all: true } }),
      this.prisma.meetingParticipant.groupBy({
        by: ['TenantId'],
        where: { Status: ParticipantStatus.Joined, Meeting: { Status: MeetingStatus.Live } },
        _count: { _all: true },
      }),
    ]);
    const byTenant = new Map<string, { tenantId: string; meetings: number; participants: number }>();
    for (const m of meetings) byTenant.set(m.TenantId, { tenantId: m.TenantId, meetings: m._count._all, participants: 0 });
    for (const p of participants) {
      const row = byTenant.get(p.TenantId) ?? { tenantId: p.TenantId, meetings: 0, participants: 0 };
      row.participants = p._count._all;
      byTenant.set(p.TenantId, row);
    }
    const rows = [...byTenant.values()].sort((a, b) => b.participants - a.participants || b.meetings - a.meetings);
    return {
      meetings: rows.reduce((sum, r) => sum + r.meetings, 0),
      participants: rows.reduce((sum, r) => sum + r.participants, 0),
      byTenant: rows,
    };
  }
}
