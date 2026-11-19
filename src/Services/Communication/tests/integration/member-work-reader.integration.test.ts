import { afterAll, beforeAll, describe, expect, it } from 'vitest';
import { randomBytes, randomUUID } from 'node:crypto';
import { PrismaClient } from '@prisma/client';
import { PrismaMemberWorkReader } from '../../src/infrastructure/persistence/prisma-member-work-reader.js';

/** Work Profile con SQL real: tickets abiertos asignados y reuniones próximas por membresía, sin cruzar oficinas. */
const prisma = new PrismaClient();
const reader = new PrismaMemberWorkReader(prisma);
const tenantId = randomUUID();
const otherTenant = randomUUID();
const agentTenant = randomUUID();
const me = randomUUID();
const now = new Date();
const inHours = (h: number) => new Date(now.getTime() + h * 3_600_000);

async function meeting(input: {
  tenant?: string;
  host: string;
  title: string;
  status?: string;
  at: Date | null;
  participant?: { userId: string; status: string };
  invitee?: { userId: string; revoked?: boolean };
}) {
  const tenant = input.tenant ?? tenantId;
  const created = await prisma.meeting.create({
    data: {
      TenantId: tenant,
      Title: input.title,
      Status: input.status ?? 'Scheduled',
      ShortCode: randomBytes(4).toString('hex'),
      HostUserId: input.host,
      CreatedByUserId: input.host,
      ScheduledForUtc: input.at,
    },
  });
  if (input.participant) {
    await prisma.meetingParticipant.create({
      data: {
        MeetingId: created.Id,
        TenantId: tenant,
        UserId: input.participant.userId,
        DisplayName: 'P',
        Role: 'Attendee',
        Status: input.participant.status,
        JoinOrder: 1,
      },
    });
  }
  if (input.invitee) {
    await prisma.meetingInvitation.create({
      data: {
        MeetingId: created.Id,
        TenantId: tenant,
        InviteeKind: 'Employee',
        InviteeUserId: input.invitee.userId,
        TokenHash: randomBytes(32).toString('hex'),
        ExpiresAtUtc: inHours(48),
        RevokedAtUtc: input.invitee.revoked ? now : null,
      },
    });
  }
}

async function ticket(subject: string, status: string, assignedAgentId: string | null, openedAtUtc: Date) {
  await prisma.supportTicket.create({
    data: {
      TenantId: tenantId,
      AgentTenantId: agentTenant,
      OpenedByUserId: randomUUID(),
      AssignedAgentId: assignedAgentId,
      ConversationId: randomUUID(),
      Subject: subject,
      Category: 'Other',
      Priority: 'Normal',
      Status: status,
      OpenedAtUtc: openedAtUtc,
    },
  });
}

beforeAll(async () => {
  await meeting({ host: me, title: 'Hosted', at: inHours(2) });
  await meeting({
    host: randomUUID(),
    title: 'As participant',
    at: inHours(3),
    participant: { userId: me, status: 'Waiting' },
  });
  await meeting({ host: randomUUID(), title: 'Invited', at: inHours(4), invitee: { userId: me } });
  await meeting({ host: me, title: 'Live now', status: 'Live', at: null });
  await meeting({ host: me, title: 'Past', at: inHours(-2) });
  await meeting({ host: me, title: 'Ended', status: 'Ended', at: inHours(5) });
  await meeting({ host: randomUUID(), title: 'Someone else', at: inHours(2) });
  await meeting({ tenant: otherTenant, host: me, title: 'Other office', at: inHours(2) });

  await ticket('Old', 'Claimed', me, inHours(-48));
  await ticket('Waiting', 'WaitingCustomer', me, inHours(-24));
  await ticket('Done', 'Resolved', me, inHours(-72));
  await ticket('Unassigned', 'Open', null, inHours(-1));
});

afterAll(async () => {
  await prisma.meeting.deleteMany({ where: { TenantId: { in: [tenantId, otherTenant] } } });
  await prisma.supportTicket.deleteMany({ where: { AgentTenantId: agentTenant } });
  await prisma.$disconnect();
});

describe('PrismaMemberWorkReader', () => {
  it('reuniones Live o agendadas desde ahora donde es host, participante o invitado, solo en su oficina', async () => {
    const rows = await reader.listUpcomingMeetings(tenantId, [me], now);

    expect(rows.map((r) => r.title).sort()).toEqual(['As participant', 'Hosted', 'Invited', 'Live now']);
    const invited = rows.find((r) => r.title === 'Invited');
    expect(invited?.invitees).toEqual([{ userId: expect.any(String), revoked: false, closed: false }]);
    expect(invited?.invitees[0]?.userId.toLowerCase()).toBe(me);
  });

  it('tickets no terminales asignados, los más antiguos primero', async () => {
    const rows = await reader.listOpenAssignedTickets(agentTenant, [me]);

    expect(rows.map((r) => [r.subject, r.status])).toEqual([
      ['Old', 'Claimed'],
      ['Waiting', 'WaitingCustomer'],
    ]);
  });
});
