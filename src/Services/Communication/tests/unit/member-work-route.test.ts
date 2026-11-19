import Fastify, { type FastifyReply, type FastifyRequest } from 'fastify';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { randomUUID } from 'node:crypto';
import { registerWorkRoutes } from '../../src/api/http/routes/work.route.js';
import type { AppContainer } from '../../src/infrastructure/container.js';
import type { AuthenticatedPrincipal } from '../../src/infrastructure/jwks/jwt-verifier.js';
import type {
  AssignedTicketRow,
  MemberWorkReader,
  UpcomingMeetingRow,
} from '../../src/application/ports/member-work-reader.js';
import type { SupportTicketRepository } from '../../src/application/ports/support-ticket-repository.js';
import { SupportTicket, type SupportTicketSnapshot } from '../../src/domain/support/support-ticket.js';
import {
  createFakeProjectionRepository,
  fakeSnapshot,
} from '../helpers/fake-user-permissions-projection-repository.js';

/** Work Profile en Communication: tickets asignados y reuniones próximas de una persona, con la visibilidad del que mira. */
const OFFICE = randomUUID();
const OTHER_OFFICE = randomUUID();
const PLATFORM = randomUUID();
const HOUR = 3_600_000;

interface TicketFixture extends AssignedTicketRow {
  readonly agentTenantId: string;
}
interface MeetingFixture extends UpcomingMeetingRow {
  readonly tenantId: string;
}

/** Lector en memoria con el mismo filtro que el de Prisma: oficina, estado y membresía. */
class MemoryWorkReader implements MemberWorkReader {
  readonly tickets: TicketFixture[] = [];
  readonly meetings: MeetingFixture[] = [];

  async listOpenAssignedTickets(
    agentTenantId: string,
    userIds: readonly string[],
  ): Promise<AssignedTicketRow[]> {
    return this.tickets
      .filter((t) => t.agentTenantId === agentTenantId && userIds.includes(t.assignedAgentId))
      .filter((t) => t.status !== 'Resolved' && t.status !== 'Closed')
      .sort((a, b) => a.openedAtUtc.getTime() - b.openedAtUtc.getTime());
  }

  async listUpcomingMeetings(
    tenantId: string,
    userIds: readonly string[],
    nowUtc: Date,
  ): Promise<UpcomingMeetingRow[]> {
    return this.meetings
      .filter((m) => m.tenantId === tenantId)
      .filter((m) => m.status === 'Live' || (m.scheduledForUtc !== null && m.scheduledForUtc >= nowUtc))
      .filter(
        (m) =>
          userIds.includes(m.hostUserId) ||
          m.participants.some((p) => userIds.includes(p.userId)) ||
          m.invitees.some((i) => userIds.includes(i.userId)),
      );
  }
}

function meeting(over: Partial<MeetingFixture> & { hostUserId: string }): MeetingFixture {
  return {
    id: randomUUID(),
    tenantId: OFFICE,
    title: 'Planning',
    status: 'Scheduled',
    scheduledForUtc: new Date(Date.now() + 2 * HOUR),
    participants: [],
    invitees: [],
    ...over,
  };
}

function ticket(over: Partial<TicketFixture> & { assignedAgentId: string }): TicketFixture {
  return {
    id: randomUUID(),
    agentTenantId: PLATFORM,
    subject: 'Cannot upload',
    status: 'Claimed',
    openedAtUtc: new Date(Date.now() - 24 * HOUR),
    ...over,
  };
}

/** Ticket completo de soporte (títulos en vivo): mismo agregado que lee `GET /communication/support/:id`. */
function supportTicket(over: Partial<SupportTicketSnapshot> = {}): SupportTicketSnapshot {
  const now = new Date();
  return {
    id: randomUUID(),
    tenantId: OFFICE,
    agentTenantId: PLATFORM,
    openedByUserId: randomUUID(),
    assignedAgentId: null,
    conversationId: randomUUID(),
    subject: 'Cannot upload',
    category: 'Technical',
    priority: 'Normal',
    status: 'Open',
    openedAtUtc: now,
    claimedAtUtc: null,
    resolvedAtUtc: null,
    closedAtUtc: null,
    updatedAtUtc: now,
    ...over,
  };
}

/** Repositorio de tickets en memoria: solo la lectura en lote por oficina de agentes. */
class MemorySupportTickets implements Pick<SupportTicketRepository, 'findManyForAgentTenant'> {
  readonly snapshots: SupportTicketSnapshot[] = [];
  calls = 0;

  async findManyForAgentTenant(agentTenantId: string, ids: readonly string[]): Promise<SupportTicket[]> {
    this.calls++;
    return this.snapshots
      .filter((s) => s.agentTenantId === agentTenantId && ids.includes(s.id))
      .map((s) => SupportTicket.rehydrate(s));
  }
}

function setup(input: {
  viewer: string;
  tenantId?: string;
  actorType?: string;
  permissions: string[];
  timeZoneId?: string;
}) {
  const reader = new MemoryWorkReader();
  const supportTickets = new MemorySupportTickets();
  const tenantId = input.tenantId ?? OFFICE;
  const container = {
    userPermissions: createFakeProjectionRepository([
      fakeSnapshot({
        userId: input.viewer,
        tenantId,
        permissions: input.permissions,
        actorType: input.actorType ?? 'TenantEmployee',
      }),
    ]),
    platform: { getPlatformTenantId: () => PLATFORM },
    tenantTimeZones: {
      async find(id: string) {
        return input.timeZoneId
          ? { tenantId: id, timeZoneId: input.timeZoneId, sourceVersion: 1, sourceUpdatedAtUtc: new Date(0) }
          : null;
      },
      async save() {},
    },
    memberWork: reader,
    supportTickets,
  } as unknown as AppContainer;

  const principal: AuthenticatedPrincipal = {
    userId: input.viewer,
    tenantId,
    actorType: input.actorType ?? 'TenantEmployee',
    permissions: [],
    permissionVersion: 1,
    sessionId: undefined,
    jti: undefined,
    raw: {},
  };

  async function send(method: 'GET' | 'POST', url: string, payload?: unknown) {
    const app = Fastify();
    app.decorate('authenticate', async function authenticate(request: FastifyRequest, _reply: FastifyReply) {
      request.principal = principal;
    });
    await registerWorkRoutes(app, container);
    await app.ready();
    const response = await app.inject(
      payload === undefined ? { method, url } : { method, url, payload: payload as object },
    );
    await app.close();
    return response;
  }

  const get = (url: string) => send('GET', url);
  const post = (url: string, payload: unknown) => send('POST', url, payload);

  return { reader, supportTickets, get, post };
}

afterEach(() => {
  vi.useRealTimers();
});

describe('GET /communication/work/:userId', () => {
  it('el propio usuario ve sus reuniones próximas con conteos y enlace del CRM', async () => {
    const me = randomUUID();
    const { reader, get } = setup({ viewer: me, permissions: ['communication.meeting.join'] });
    const hosted = meeting({ hostUserId: me, title: 'Kickoff' });
    reader.meetings.push(
      hosted,
      meeting({ hostUserId: randomUUID(), participants: [{ userId: me, status: 'Waiting' }] }),
      meeting({ hostUserId: randomUUID(), invitees: [{ userId: me, revoked: false, closed: false }] }),
      meeting({
        hostUserId: randomUUID(),
        status: 'Live',
        scheduledForUtc: null,
        participants: [{ userId: me, status: 'Joined' }],
      }),
      // No cuentan: lo sacaron, invitación revocada, de otra persona.
      meeting({ hostUserId: randomUUID(), participants: [{ userId: me, status: 'Removed' }] }),
      meeting({ hostUserId: randomUUID(), invitees: [{ userId: me, revoked: true, closed: false }] }),
      meeting({ hostUserId: randomUUID() }),
    );

    const response = await get(`/communication/work/${me}`);

    expect(response.statusCode).toBe(200);
    const body = response.json();
    expect(body).toMatchObject({ provider: 'communication', module: 'communication' });
    expect(body.counts).toEqual([{ kind: 'meetings', open: 4, overdue: 0, dueSoon: 3, waiting: 0 }]);
    expect(body.top).toHaveLength(4);
    expect(body.top).toContainEqual({
      id: hosted.id,
      kind: 'meetings',
      title: 'Kickoff',
      customerId: null,
      dueAtUtc: hosted.scheduledForUtc?.toISOString(),
      dueDate: null,
      status: 'Scheduled',
      link: `/meetings?meetingId=${hosted.id}`,
    });
  });

  it('el top se corta en 5', async () => {
    const me = randomUUID();
    const { reader, get } = setup({ viewer: me, permissions: ['communication.meeting.join'] });
    for (let i = 1; i <= 7; i++) {
      reader.meetings.push(meeting({ hostUserId: me, scheduledForUtc: new Date(Date.now() + i * HOUR) }));
    }

    const body = (await get(`/communication/work/${me}`)).json();

    expect(body.counts[0].open).toBe(7);
    expect(body.top).toHaveLength(5);
  });

  it('otra persona sin users.work.view: 403', async () => {
    const me = randomUUID();
    const { get } = setup({ viewer: me, permissions: ['communication.meeting.join'] });

    const response = await get(`/communication/work/${randomUUID()}`);

    expect(response.statusCode).toBe(403);
    expect(response.json().code).toBe('Auth.Forbidden');
  });

  it('sin permiso de lectura del módulo, ni siquiera lo propio: 403', async () => {
    const me = randomUUID();
    const { get } = setup({ viewer: me, permissions: ['users.work.view'] });

    const response = await get(`/communication/work/${me}`);

    expect(response.statusCode).toBe(403);
  });

  it('un cliente del portal nunca entra', async () => {
    const me = randomUUID();
    const { get } = setup({
      viewer: me,
      actorType: 'CustomerPortal',
      permissions: ['communication.meeting.join'],
    });

    const response = await get(`/communication/work/${me}`);

    expect(response.statusCode).toBe(403);
  });

  it('con users.work.view cuenta todas las reuniones de la persona, pero solo titula las que el que mira también ve', async () => {
    const viewer = randomUUID();
    const target = randomUUID();
    const { reader, get } = setup({ viewer, permissions: ['communication.meeting.join', 'users.work.view'] });
    const shared = meeting({
      hostUserId: target,
      title: 'Team sync',
      participants: [{ userId: viewer, status: 'Waiting' }],
    });
    const privateOne = meeting({
      hostUserId: target,
      title: 'Private 1:1',
      participants: [{ userId: randomUUID(), status: 'Waiting' }],
    });
    reader.meetings.push(shared, privateOne);

    const response = await get(`/communication/work/${target}`);

    expect(response.statusCode).toBe(200);
    const body = response.json();
    expect(body.counts).toEqual([{ kind: 'meetings', open: 2, overdue: 0, dueSoon: 2, waiting: 0 }]);
    expect(body.top.map((i: { id: string }) => i.id)).toEqual([shared.id]);
    expect(JSON.stringify(body)).not.toContain('Private 1:1');
  });

  it('otra oficina nunca: solo cuenta lo del tenant del JWT', async () => {
    const me = randomUUID();
    const { reader, get } = setup({ viewer: me, permissions: ['communication.meeting.join'] });
    reader.meetings.push(
      meeting({ hostUserId: me }),
      meeting({ hostUserId: me, tenantId: OTHER_OFFICE, title: 'Elsewhere' }),
    );

    const body = (await get(`/communication/work/${me}`)).json();

    expect(body.counts[0].open).toBe(1);
    expect(JSON.stringify(body)).not.toContain('Elsewhere');
  });

  it('tickets de soporte asignados (oficina de agentes): abiertos, esperando al cliente y los más antiguos primero', async () => {
    const me = randomUUID();
    const { reader, get } = setup({
      viewer: me,
      tenantId: PLATFORM,
      permissions: ['communication.support.agent'],
    });
    const oldest = ticket({
      assignedAgentId: me,
      subject: 'Oldest',
      openedAtUtc: new Date(Date.now() - 72 * HOUR),
    });
    reader.tickets.push(
      ticket({ assignedAgentId: me, status: 'WaitingCustomer' }),
      oldest,
      ticket({ assignedAgentId: me, status: 'WaitingAgent' }),
      ticket({ assignedAgentId: me, status: 'Resolved' }),
      ticket({ assignedAgentId: me, status: 'Closed' }),
      ticket({ assignedAgentId: randomUUID() }),
    );

    const response = await get(`/communication/work/${me}`);

    expect(response.statusCode).toBe(200);
    const body = response.json();
    expect(body.counts).toEqual([{ kind: 'support-tickets', open: 3, overdue: 0, dueSoon: 0, waiting: 1 }]);
    expect(body.top[0]).toEqual({
      id: oldest.id,
      kind: 'support-tickets',
      title: 'Oldest',
      customerId: null,
      dueAtUtc: null,
      dueDate: null,
      status: 'Claimed',
      link: `/support?ticketId=${oldest.id}`,
    });
  });

  it('en una oficina cliente no hay bloque de soporte: los agentes viven en la oficina de plataforma', async () => {
    const me = randomUUID();
    const { reader, get } = setup({
      viewer: me,
      permissions: ['communication.meeting.join', 'communication.support.agent'],
    });
    reader.tickets.push(ticket({ assignedAgentId: me, agentTenantId: OFFICE }));

    const body = (await get(`/communication/work/${me}`)).json();

    expect(body.counts.map((c: { kind: string }) => c.kind)).toEqual(['meetings']);
  });

  it('tickets de otro agente: cuenta, pero sin asunto si el que mira no podría abrirlos', async () => {
    const viewer = randomUUID();
    const target = randomUUID();
    const { reader, get } = setup({
      viewer,
      tenantId: PLATFORM,
      permissions: ['communication.support.agent', 'users.work.view'],
    });
    reader.tickets.push(ticket({ assignedAgentId: target, subject: 'Secret subject' }));

    const body = (await get(`/communication/work/${target}`)).json();

    expect(body.counts[0]).toMatchObject({ kind: 'support-tickets', open: 1 });
    expect(body.top).toEqual([]);
    expect(JSON.stringify(body)).not.toContain('Secret subject');
  });

  it('dueSoon usa los 7 días de la zona de la oficina, no los de UTC', async () => {
    // 2026-11-18 07:30 UTC = 17-nov 23:30 en Los Ángeles: la ventana termina el 24-nov 00:00 PST (08:00 UTC).
    vi.useFakeTimers({ toFake: ['Date'], now: new Date('2026-11-18T07:30:00Z') });
    const me = randomUUID();
    const { reader, get } = setup({
      viewer: me,
      permissions: ['communication.meeting.join'],
      timeZoneId: 'America/Los_Angeles',
    });
    reader.meetings.push(
      meeting({ hostUserId: me, scheduledForUtc: new Date('2026-11-24T07:00:00Z') }),
      meeting({ hostUserId: me, scheduledForUtc: new Date('2026-11-24T09:00:00Z') }),
    );

    const body = (await get(`/communication/work/${me}`)).json();

    expect(body.counts).toEqual([{ kind: 'meetings', open: 2, overdue: 0, dueSoon: 1, waiting: 0 }]);
  });

  it('un userId que no es GUID: 400', async () => {
    const me = randomUUID();
    const { get } = setup({ viewer: me, permissions: ['communication.meeting.join'] });

    const response = await get('/communication/work/not-a-guid');

    expect(response.statusCode).toBe(400);
  });
});

describe('GET /communication/work/summary', () => {
  it('exige users.work.view', async () => {
    const me = randomUUID();
    const { get } = setup({ viewer: me, permissions: ['communication.meeting.join'] });

    const response = await get(`/communication/work/summary?userIds=${me}`);

    expect(response.statusCode).toBe(403);
  });

  it('devuelve una fila por persona en el orden pedido, sin repetidos', async () => {
    const viewer = randomUUID();
    const a = randomUUID();
    const b = randomUUID();
    const idle = randomUUID();
    const { reader, get } = setup({ viewer, permissions: ['communication.meeting.join', 'users.work.view'] });
    reader.meetings.push(
      meeting({ hostUserId: a }),
      meeting({ hostUserId: a, participants: [{ userId: b, status: 'Waiting' }] }),
      meeting({ hostUserId: b, tenantId: OTHER_OFFICE }),
    );

    const response = await get(`/communication/work/summary?userIds=${b},${a},${idle},${a.toUpperCase()}`);

    expect(response.statusCode).toBe(200);
    expect(response.json()).toEqual({
      rows: [
        { userId: b, open: 1, overdue: 0 },
        { userId: a, open: 2, overdue: 0 },
        { userId: idle, open: 0, overdue: 0 },
      ],
    });
  });

  it('suma tickets y reuniones en la oficina de agentes', async () => {
    const viewer = randomUUID();
    const agent = randomUUID();
    const { reader, get } = setup({
      viewer,
      tenantId: PLATFORM,
      permissions: ['communication.meeting.join', 'communication.support.agent', 'users.work.view'],
    });
    reader.tickets.push(
      ticket({ assignedAgentId: agent }),
      ticket({ assignedAgentId: agent, status: 'Closed' }),
    );
    reader.meetings.push(meeting({ hostUserId: agent, tenantId: PLATFORM }));

    const body = (await get(`/communication/work/summary?userIds=${agent}`)).json();

    expect(body.rows).toEqual([{ userId: agent, open: 2, overdue: 0 }]);
  });

  it.each(['', 'nope', Array.from({ length: 101 }, () => randomUUID()).join(',')])(
    'rechaza userIds inválidos (%#)',
    async (raw) => {
      const viewer = randomUUID();
      const { get } = setup({ viewer, permissions: ['communication.meeting.join', 'users.work.view'] });

      const response = await get(`/communication/work/summary?userIds=${raw}`);

      expect(response.statusCode).toBe(400);
      expect(response.json().code).toBe('Communication.Work.InvalidUserIds');
    },
  );
});

describe('GET /communication/work/:userId/items', () => {
  it('pagina las reuniones en el mismo orden que el top, con los datos de paginado', async () => {
    const me = randomUUID();
    const { reader, get } = setup({ viewer: me, permissions: ['communication.meeting.join'] });
    const live = meeting({
      hostUserId: me,
      status: 'Live',
      scheduledForUtc: null,
      participants: [{ userId: me, status: 'Joined' }],
    });
    const scheduled = Array.from({ length: 7 }, (_, i) =>
      meeting({ hostUserId: me, scheduledForUtc: new Date(Date.now() + (7 - i) * HOUR) }),
    );
    reader.meetings.push(...scheduled, live);

    const response = await get(`/communication/work/${me}/items?kind=meetings&page=2&size=3`);

    expect(response.statusCode).toBe(200);
    const body = response.json();
    const ordered = [live, ...[...scheduled].reverse()];
    expect(body.items.map((i: { id: string }) => i.id)).toEqual(ordered.slice(3, 6).map((m) => m.id));
    expect(body).toMatchObject({
      page: 2,
      size: 3,
      totalCount: 8,
      totalPages: 3,
      hasMore: true,
      hasPrevious: true,
    });
    const first = ordered[3]?.id;
    expect(body.items[0]).toMatchObject({ kind: 'meetings', link: `/meetings?meetingId=${first}` });
  });

  it('size por defecto 20 y nunca más de 50', async () => {
    const me = randomUUID();
    const { reader, get } = setup({ viewer: me, permissions: ['communication.meeting.join'] });
    for (let i = 1; i <= 55; i++) {
      reader.meetings.push(meeting({ hostUserId: me, scheduledForUtc: new Date(Date.now() + i * HOUR) }));
    }

    const byDefault = (await get(`/communication/work/${me}/items?kind=meetings`)).json();
    const capped = (await get(`/communication/work/${me}/items?kind=meetings&size=500`)).json();

    expect(byDefault).toMatchObject({ page: 1, size: 20, totalCount: 55, hasMore: true, hasPrevious: false });
    expect(byDefault.items).toHaveLength(20);
    expect(capped.size).toBe(50);
    expect(capped.items).toHaveLength(50);
  });

  it.each(['', '?kind=tasks', '?kind=meetings&page=0', '?kind=meetings&size=abc'])(
    'kind o paginado inválido: 400 (%s)',
    async (query) => {
      const me = randomUUID();
      const { get } = setup({ viewer: me, permissions: ['communication.meeting.join'] });

      const response = await get(`/communication/work/${me}/items${query}`);

      expect(response.statusCode).toBe(400);
      expect(response.json().code).toMatch(/^Communication\.Work\./);
    },
  );

  it('con users.work.view solo lista lo que el que mira puede abrir; totalCount = lo visible', async () => {
    const viewer = randomUUID();
    const target = randomUUID();
    const { reader, get } = setup({ viewer, permissions: ['communication.meeting.join', 'users.work.view'] });
    const shared = meeting({ hostUserId: target, participants: [{ userId: viewer, status: 'Waiting' }] });
    reader.meetings.push(shared, meeting({ hostUserId: target, title: 'Private 1:1' }));

    const response = await get(`/communication/work/${target}/items?kind=meetings`);

    expect(response.statusCode).toBe(200);
    const body = response.json();
    expect(body.items.map((i: { id: string }) => i.id)).toEqual([shared.id]);
    expect(body.totalCount).toBe(1);
    expect(JSON.stringify(body)).not.toContain('Private 1:1');
  });

  it('otra persona sin users.work.view: 403; portal: 403', async () => {
    const me = randomUUID();
    const employee = setup({ viewer: me, permissions: ['communication.meeting.join'] });
    const portal = setup({
      viewer: me,
      actorType: 'CustomerPortal',
      permissions: ['communication.meeting.join'],
    });

    expect((await employee.get(`/communication/work/${randomUUID()}/items?kind=meetings`)).statusCode).toBe(
      403,
    );
    expect((await portal.get(`/communication/work/${me}/items?kind=meetings`)).statusCode).toBe(403);
  });

  it('tickets asignados en la oficina de agentes: los más antiguos primero, sin cerrados', async () => {
    const me = randomUUID();
    const { reader, get } = setup({
      viewer: me,
      tenantId: PLATFORM,
      permissions: ['communication.support.agent'],
    });
    const newer = ticket({ assignedAgentId: me, openedAtUtc: new Date(Date.now() - HOUR) });
    const oldest = ticket({ assignedAgentId: me, openedAtUtc: new Date(Date.now() - 72 * HOUR) });
    reader.tickets.push(newer, oldest, ticket({ assignedAgentId: me, status: 'Closed' }));

    const body = (await get(`/communication/work/${me}/items?kind=support-tickets`)).json();

    expect(body.items.map((i: { id: string }) => i.id)).toEqual([oldest.id, newer.id]);
    expect(body.totalCount).toBe(2);
  });

  it('un tipo que el que mira no puede leer: 403', async () => {
    const me = randomUUID();
    const { get } = setup({
      viewer: me,
      permissions: ['communication.meeting.join', 'communication.support.agent'],
    });

    const response = await get(`/communication/work/${me}/items?kind=support-tickets`);

    expect(response.statusCode).toBe(403);
  });
});

describe('POST /communication/work/titles', () => {
  const ref = (id: string, resourceType = 'support_ticket') => ({ resourceType, id });

  it('el agente obtiene el asunto de los tickets que podría abrir, en una sola lectura', async () => {
    const me = randomUUID();
    const { supportTickets, post } = setup({
      viewer: me,
      tenantId: PLATFORM,
      permissions: ['communication.support.agent'],
    });
    const mine = supportTicket({ assignedAgentId: me, status: 'Claimed', subject: 'W-2 missing' });
    const unclaimed = supportTicket({ subject: 'New one' });
    const othersAgent = supportTicket({
      assignedAgentId: randomUUID(),
      status: 'Claimed',
      subject: 'Secret',
    });
    supportTickets.snapshots.push(mine, unclaimed, othersAgent);

    const response = await post('/communication/work/titles', {
      items: [ref(mine.id), ref(unclaimed.id), ref(othersAgent.id), ref(randomUUID()), ref(mine.id, 'task')],
    });

    expect(response.statusCode).toBe(200);
    expect(response.json()).toEqual({
      items: [
        { resourceType: 'support_ticket', id: mine.id, title: 'W-2 missing' },
        { resourceType: 'support_ticket', id: unclaimed.id, title: 'New one' },
      ],
    });
    expect(supportTickets.calls).toBe(1);
  });

  it('PlatformAdmin ve el asunto de cualquier ticket de la oficina de agentes', async () => {
    const me = randomUUID();
    const { supportTickets, post } = setup({
      viewer: me,
      tenantId: PLATFORM,
      actorType: 'PlatformAdmin',
      permissions: [],
    });
    const any = supportTicket({ assignedAgentId: randomUUID(), status: 'Claimed', subject: 'Escalated' });
    supportTickets.snapshots.push(any);

    const body = (await post('/communication/work/titles', { items: [ref(any.id)] })).json();

    expect(body.items).toEqual([{ resourceType: 'support_ticket', id: any.id, title: 'Escalated' }]);
  });

  it('en una oficina cliente la respuesta es vacía y no consulta tickets', async () => {
    const me = randomUUID();
    const { supportTickets, post } = setup({ viewer: me, permissions: ['communication.support.agent'] });
    const opened = supportTicket({ openedByUserId: me });
    supportTickets.snapshots.push(opened);

    const response = await post('/communication/work/titles', { items: [ref(opened.id)] });

    expect(response.statusCode).toBe(200);
    expect(response.json()).toEqual({ items: [] });
    expect(supportTickets.calls).toBe(0);
  });

  it('recorta el asunto a una línea de 120 caracteres', async () => {
    const me = randomUUID();
    const { supportTickets, post } = setup({
      viewer: me,
      tenantId: PLATFORM,
      permissions: ['communication.support.agent'],
    });
    const long = supportTicket({
      assignedAgentId: me,
      status: 'Claimed',
      subject: `Help  me\n${'x'.repeat(200)}`,
    });
    supportTickets.snapshots.push(long);

    const body = (await post('/communication/work/titles', { items: [ref(long.id)] })).json();

    const title: string = body.items[0].title;
    expect(title).toHaveLength(120);
    expect(title.startsWith('Help me x')).toBe(true);
    expect(title.endsWith('…')).toBe(true);
  });

  it.each([
    { items: Array.from({ length: 101 }, () => ({ resourceType: 'support_ticket', id: randomUUID() })) },
    {},
    { items: [{ resourceType: 'support_ticket', id: 'not-a-guid' }] },
  ])('más de 100 o cuerpo inválido: 400 (%#)', async (payload) => {
    const me = randomUUID();
    const { post } = setup({ viewer: me, tenantId: PLATFORM, permissions: ['communication.support.agent'] });

    const response = await post('/communication/work/titles', payload);

    expect(response.statusCode).toBe(400);
    expect(response.json().code).toBe('Communication.Work.InvalidTitles');
  });

  it('un cliente del portal nunca entra', async () => {
    const me = randomUUID();
    const { post } = setup({ viewer: me, actorType: 'CustomerPortal', permissions: [] });

    const response = await post('/communication/work/titles', { items: [] });

    expect(response.statusCode).toBe(403);
  });
});
