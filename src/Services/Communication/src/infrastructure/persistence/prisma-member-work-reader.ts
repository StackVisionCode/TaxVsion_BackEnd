import type { PrismaClient } from '@prisma/client';
import { isSupportStatus } from '../../domain/support/support-enums.js';
import type {
  AssignedTicketRow,
  MemberWorkReader,
  UpcomingMeetingRow,
} from '../../application/ports/member-work-reader.js';

/** Work Profile: solo columnas de conteo y título; nunca descripción, mensajes ni chats. */
export class PrismaMemberWorkReader implements MemberWorkReader {
  constructor(private readonly prisma: PrismaClient) {}

  async listOpenAssignedTickets(
    agentTenantId: string,
    userIds: readonly string[],
  ): Promise<AssignedTicketRow[]> {
    if (userIds.length === 0) return [];
    const rows = await this.prisma.supportTicket.findMany({
      where: {
        AgentTenantId: agentTenantId,
        AssignedAgentId: { in: [...userIds] },
        Status: { notIn: ['Resolved', 'Closed'] },
      },
      select: { Id: true, Subject: true, Status: true, AssignedAgentId: true, OpenedAtUtc: true },
      orderBy: { OpenedAtUtc: 'asc' },
    });
    return rows.flatMap((r) =>
      r.AssignedAgentId && isSupportStatus(r.Status)
        ? [
            {
              id: r.Id,
              subject: r.Subject,
              status: r.Status,
              assignedAgentId: r.AssignedAgentId,
              openedAtUtc: r.OpenedAtUtc,
            },
          ]
        : [],
    );
  }

  async listUpcomingMeetings(
    tenantId: string,
    userIds: readonly string[],
    nowUtc: Date,
  ): Promise<UpcomingMeetingRow[]> {
    if (userIds.length === 0) return [];
    const ids = [...userIds];
    const rows = await this.prisma.meeting.findMany({
      where: {
        TenantId: tenantId,
        AND: [
          { OR: [{ Status: 'Live' }, { Status: 'Scheduled', ScheduledForUtc: { gte: nowUtc } }] },
          {
            OR: [
              { HostUserId: { in: ids } },
              { Participants: { some: { UserId: { in: ids } } } },
              { Invitations: { some: { TenantId: tenantId, InviteeUserId: { in: ids } } } },
            ],
          },
        ],
      },
      select: {
        Id: true,
        Title: true,
        Status: true,
        HostUserId: true,
        ScheduledForUtc: true,
        Participants: { select: { UserId: true, Status: true } },
        Invitations: {
          where: { InviteeUserId: { not: null } },
          select: { InviteeUserId: true, RevokedAtUtc: true, ClosedAtUtc: true },
        },
      },
      orderBy: [{ ScheduledForUtc: 'asc' }, { CreatedAtUtc: 'asc' }],
    });
    return rows.map((r) => ({
      id: r.Id,
      title: r.Title,
      status: r.Status === 'Live' ? 'Live' : 'Scheduled',
      hostUserId: r.HostUserId,
      scheduledForUtc: r.ScheduledForUtc,
      participants: r.Participants.map((p) => ({ userId: p.UserId, status: p.Status })),
      invitees: r.Invitations.flatMap((i) =>
        i.InviteeUserId
          ? [{ userId: i.InviteeUserId, revoked: i.RevokedAtUtc !== null, closed: i.ClosedAtUtc !== null }]
          : [],
      ),
    }));
  }
}
