import Fastify, { type FastifyReply, type FastifyRequest } from 'fastify';
import { describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { getVisibleFiles } from '../../src/application/use-cases/file-visibility.js';
import { registerFileRoutes } from '../../src/api/http/routes/files.route.js';
import { SupportTicket } from '../../src/domain/support/support-ticket.js';
import type { Conversation } from '../../src/domain/conversations/conversation.js';
import type { AppContainer } from '../../src/infrastructure/container.js';
import type { AuthenticatedPrincipal } from '../../src/infrastructure/jwks/jwt-verifier.js';
import {
  createFakeProjectionRepository,
  fakeSnapshot,
} from '../helpers/fake-user-permissions-projection-repository.js';
import {
  conversationWith,
  conversationsOf,
  FakeFileLibraryReadRepository,
} from '../helpers/chat-file-library.js';

const u = () => randomUUID();

function world(permissions: string[] = [], actorType = 'TenantEmployee') {
  const tenantId = u();
  const me = u();
  const repo = new FakeFileLibraryReadRepository();
  const userPermissions = createFakeProjectionRepository([
    fakeSnapshot({ userId: me, permissions, actorType }),
  ]);
  const attach = (conversation: Conversation, fileId = u()) => {
    repo.attachments.push({ tenantId: conversation.tenantId, fileId, conversationId: conversation.id });
    return fileId;
  };
  return {
    tenantId,
    me,
    repo,
    userPermissions,
    attach,
    viewer: { tenantId, userId: me, actorType, canReadChat: true },
  };
}

describe('getVisibleFiles — rama de chat', () => {
  it('devuelve el archivo de mi chat con el otro participante, el cliente y la marca de participantes', async () => {
    const w = world();
    const customerId = u();
    const chat = conversationWith({
      tenantId: w.tenantId,
      kind: 'Direct',
      participants: [
        { userId: w.me, displayName: 'Ana' },
        { userId: u(), displayName: 'Carla Client', actorType: 'CustomerPortal', customerId },
      ],
    });
    const fileId = w.attach(chat);

    const items = await getVisibleFiles(w.viewer, [fileId], {
      fileLibrary: w.repo,
      conversations: conversationsOf(chat),
      userPermissions: w.userPermissions,
    });

    expect(items).toEqual([
      {
        fileId,
        resourceType: 'conversation',
        resourceId: chat.id,
        title: 'Carla Client',
        customerId,
        context: null,
        canDownload: true,
        restrictedTo: 'participants',
      },
    ]);
  });

  it('omite lo que no ve: conversación ajena, grupo del que salió, otro tenant y archivo desconocido', async () => {
    const w = world();
    const foreign = conversationWith({
      tenantId: w.tenantId,
      kind: 'Group',
      title: 'Others',
      participants: [{ userId: u() }, { userId: u() }],
    });
    const left = conversationWith({
      tenantId: w.tenantId,
      kind: 'Group',
      title: 'Left',
      participants: [{ userId: u() }, { userId: w.me, isRemoved: true }],
    });
    const otherTenant = conversationWith({
      tenantId: u(),
      kind: 'Group',
      title: 'X',
      participants: [{ userId: w.me }, { userId: u() }],
    });
    const ids = [w.attach(foreign), w.attach(left), w.attach(otherTenant), u()];

    const items = await getVisibleFiles(w.viewer, ids, {
      fileLibrary: w.repo,
      conversations: conversationsOf(foreign, left, otherTenant),
      userPermissions: w.userPermissions,
    });

    expect(items).toEqual([]);
  });

  it('un TenantAdmin que no es miembro no recibe nada, aunque vea a todos los clientes', async () => {
    const w = world(['customers.view_all', 'communication.chat.moderate'], 'TenantAdmin');
    const chat = conversationWith({
      tenantId: w.tenantId,
      kind: 'Direct',
      participants: [{ userId: u() }, { userId: u(), actorType: 'CustomerPortal', customerId: u() }],
    });

    const items = await getVisibleFiles({ ...w.viewer, actorType: 'TenantAdmin' }, [w.attach(chat)], {
      fileLibrary: w.repo,
      conversations: conversationsOf(chat),
      userPermissions: w.userPermissions,
    });

    expect(items).toEqual([]);
  });

  it('un chat de cliente cerrado por un retiro lo ve quien ve a ese cliente, sin marca de participantes', async () => {
    const w = world(['customers.view_all']);
    const customerId = u();
    const closed = conversationWith({
      tenantId: w.tenantId,
      kind: 'Direct',
      isArchived: true,
      participants: [
        { userId: u(), displayName: 'Former staff', isRemoved: true },
        { userId: u(), displayName: 'Carla Client', actorType: 'CustomerPortal', customerId },
      ],
    });
    const fileId = w.attach(closed);

    const items = await getVisibleFiles(w.viewer, [fileId], {
      fileLibrary: w.repo,
      conversations: conversationsOf(closed),
      userPermissions: w.userPermissions,
    });

    expect(items).toEqual([
      expect.objectContaining({
        fileId,
        resourceType: 'conversation',
        title: 'Carla Client',
        customerId,
        restrictedTo: null,
      }),
    ]);
  });

  it('soporte abre el ticket con su asunto; reunión abre la reunión con su título; grupo lleva su nombre', async () => {
    const w = world();
    const ticketId = u();
    const meetingId = u();
    const support = conversationWith({
      tenantId: w.tenantId,
      kind: 'Support',
      ticketId,
      participants: [{ userId: w.me }, { userId: u() }],
    });
    const meeting = conversationWith({
      tenantId: w.tenantId,
      kind: 'Meeting',
      meetingId,
      title: 'Quarterly review',
      participants: [{ userId: w.me }],
    });
    const group = conversationWith({
      tenantId: w.tenantId,
      kind: 'Group',
      title: 'Tax team',
      participants: [{ userId: w.me }, { userId: u() }],
    });
    const ticket = SupportTicket.rehydrate({
      id: ticketId,
      tenantId: w.tenantId,
      agentTenantId: u(),
      openedByUserId: w.me,
      assignedAgentId: null,
      conversationId: support.id,
      subject: 'Cannot upload W-2',
      category: 'Technical',
      priority: 'Normal',
      status: 'Open',
      openedAtUtc: new Date(0),
      claimedAtUtc: null,
      resolvedAtUtc: null,
      closedAtUtc: null,
      updatedAtUtc: new Date(0),
    });
    const [s, m, g] = [w.attach(support), w.attach(meeting), w.attach(group)];

    const items = await getVisibleFiles(w.viewer, [s, m, g], {
      fileLibrary: w.repo,
      conversations: conversationsOf(support, meeting, group),
      supportTickets: { findById: async (id: string) => (id === ticketId ? ticket : null) },
      userPermissions: w.userPermissions,
    });
    const byFile = new Map(items.map((i) => [i.fileId, i]));

    expect(byFile.get(s)).toMatchObject({
      resourceType: 'support_ticket',
      resourceId: ticketId,
      title: 'Cannot upload W-2',
    });
    expect(byFile.get(m)).toMatchObject({
      resourceType: 'meeting',
      resourceId: meetingId,
      title: 'Quarterly review',
    });
    expect(byFile.get(g)).toMatchObject({
      resourceType: 'conversation',
      resourceId: group.id,
      title: 'Tax team',
    });
  });

  it('sin communication.chat.reply no responde la rama de chat', async () => {
    const w = world();
    const chat = conversationWith({
      tenantId: w.tenantId,
      kind: 'Group',
      title: 'G',
      participants: [{ userId: w.me }, { userId: u() }],
    });

    const items = await getVisibleFiles({ ...w.viewer, canReadChat: false }, [w.attach(chat)], {
      fileLibrary: w.repo,
      conversations: conversationsOf(chat),
      userPermissions: w.userPermissions,
    });

    expect(items).toEqual([]);
  });
});

describe('POST /communication/files/visibility', () => {
  async function post(actorType: string, permissions: string[], body: unknown) {
    const principal: AuthenticatedPrincipal = {
      userId: u(),
      tenantId: u(),
      actorType,
      permissions: [],
      permissionVersion: 1,
      sessionId: undefined,
      jti: undefined,
      raw: {},
    };
    const container = {
      userPermissions: createFakeProjectionRepository([
        fakeSnapshot({ userId: principal.userId, permissions, actorType }),
      ]),
      fileLibrary: new FakeFileLibraryReadRepository(),
      conversations: conversationsOf(),
    } as unknown as AppContainer;
    const app = Fastify();
    app.decorate('authenticate', async (request: FastifyRequest, _reply: FastifyReply) => {
      request.principal = principal;
    });
    await registerFileRoutes(app, container);
    await app.ready();
    const response = await app.inject({
      method: 'POST',
      url: '/communication/files/visibility',
      payload: body as object,
    });
    await app.close();
    return response;
  }

  it('responde 200 con items para el personal con chat.reply', async () => {
    const response = await post('TenantEmployee', ['communication.chat.reply'], {
      fileIds: [u()],
      purpose: 'list',
    });

    expect(response.statusCode).toBe(200);
    expect(response.json()).toEqual({ items: [] });
  });

  it('más de 100 ids → 400', async () => {
    const response = await post('TenantEmployee', ['communication.chat.reply'], {
      fileIds: Array.from({ length: 101 }, u),
    });

    expect(response.statusCode).toBe(400);
  });

  it('el portal y PlatformAdmin → 403; sin ninguno de los permisos de entrada → 403', async () => {
    expect((await post('CustomerPortal', ['communication.chat.reply'], { fileIds: [] })).statusCode).toBe(
      403,
    );
    expect((await post('PlatformAdmin', [], { fileIds: [] })).statusCode).toBe(403);
    expect((await post('TenantEmployee', ['customers.view_all'], { fileIds: [] })).statusCode).toBe(403);
  });
});
