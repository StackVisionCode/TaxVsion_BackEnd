import Fastify, { type FastifyReply, type FastifyRequest } from 'fastify';
import { describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { getFileScope } from '../../src/application/use-cases/file-scope.js';
import { registerFileRoutes } from '../../src/api/http/routes/files.route.js';
import type { AppContainer } from '../../src/infrastructure/container.js';
import type { AuthenticatedPrincipal } from '../../src/infrastructure/jwks/jwt-verifier.js';
import {
  createFakeProjectionRepository,
  fakeSnapshot,
} from '../helpers/fake-user-permissions-projection-repository.js';
import { FakeFileLibraryReadRepository } from '../helpers/chat-file-library.js';

const u = () => randomUUID();
const at = (minute: number) => new Date(Date.UTC(2026, 10, 20, 12, minute));

function viewer(permissions: string[], actorType = 'TenantEmployee') {
  const userId = u();
  return {
    tenantId: u(),
    userId,
    actorType,
    canReadChat: true,
    userPermissions: createFakeProjectionRepository([fakeSnapshot({ userId, permissions, actorType })]),
  };
}

describe('getFileScope — conversaciones de chat', () => {
  it('devuelve los chats donde participo con adjuntos, el más activo primero, sin llamadas ni reuniones', async () => {
    const v = viewer([]);
    const repo = new FakeFileLibraryReadRepository();
    const [older, newer] = [u(), u()];
    repo.memberScope = [
      { conversationId: newer, lastActivityAtUtc: at(30) },
      { conversationId: older, lastActivityAtUtc: at(10) },
    ];

    const scope = await getFileScope(v, { fileLibrary: repo, userPermissions: v.userPermissions });

    expect(scope).toEqual({ conversationIds: [newer, older], callIds: [], meetingIds: [], truncated: [] });
    expect(repo.memberCalls).toEqual([{ userId: v.userId, take: 2001 }]);
  });

  it('suma los chats de cliente cerrados que puede leer: todos con customers.view_all, solo asignados sin él', async () => {
    const all = viewer(['customers.view_all']);
    const restricted = viewer([]);
    const repoAll = new FakeFileLibraryReadRepository();
    const repoRestricted = new FakeFileLibraryReadRepository();
    const [mine, closed] = [u(), u()];
    for (const repo of [repoAll, repoRestricted]) {
      repo.memberScope = [{ conversationId: mine, lastActivityAtUtc: at(5) }];
      repo.closedScope = [
        { conversationId: closed, lastActivityAtUtc: at(20) },
        { conversationId: mine, lastActivityAtUtc: at(5) },
      ];
    }

    const seen = await getFileScope(all, { fileLibrary: repoAll, userPermissions: all.userPermissions });
    const assigned = await getFileScope(restricted, {
      fileLibrary: repoRestricted,
      userPermissions: restricted.userPermissions,
    });

    expect(seen.conversationIds).toEqual([closed, mine]);
    expect(repoAll.closedCalls).toEqual([{ assignedOnly: false }]);
    expect(assigned.conversationIds).toEqual([closed, mine]);
    expect(repoRestricted.closedCalls).toEqual([{ assignedOnly: true }]);
  });

  it('sin communication.chat.reply la lista va vacía y no se consulta nada', async () => {
    const v = { ...viewer([]), canReadChat: false };
    const repo = new FakeFileLibraryReadRepository();
    repo.memberScope = [{ conversationId: u(), lastActivityAtUtc: at(1) }];

    const scope = await getFileScope(v, { fileLibrary: repo, userPermissions: v.userPermissions });

    expect(scope).toEqual({ conversationIds: [], callIds: [], meetingIds: [], truncated: [] });
    expect(repo.memberCalls).toEqual([]);
  });

  it('se corta en 2 000 con los más activos y lo avisa en truncated', async () => {
    const v = viewer([]);
    const repo = new FakeFileLibraryReadRepository();
    repo.memberScope = Array.from({ length: 2_001 }, (_, i) => ({
      conversationId: u(),
      lastActivityAtUtc: new Date(Date.UTC(2026, 0, 1) + (2_001 - i) * 60_000),
    }));

    const scope = await getFileScope(v, { fileLibrary: repo, userPermissions: v.userPermissions });

    expect(scope.conversationIds).toHaveLength(2_000);
    expect(scope.conversationIds).toEqual(repo.memberScope.slice(0, 2_000).map((r) => r.conversationId));
    expect(scope.truncated).toEqual(['conversations']);
  });
});

describe('GET /communication/files/scope', () => {
  async function call(
    principal: AuthenticatedPrincipal,
    permissions: string[],
    repo = new FakeFileLibraryReadRepository(),
  ) {
    const container = {
      userPermissions: createFakeProjectionRepository([
        fakeSnapshot({ userId: principal.userId, permissions, actorType: principal.actorType }),
      ]),
      fileLibrary: repo,
    } as unknown as AppContainer;
    const app = Fastify();
    app.decorate('authenticate', async (request: FastifyRequest, _reply: FastifyReply) => {
      request.principal = principal;
    });
    await registerFileRoutes(app, container);
    await app.ready();
    const response = await app.inject({ method: 'GET', url: '/communication/files/scope' });
    await app.close();
    return response;
  }

  const principal = (actorType: string): AuthenticatedPrincipal => ({
    userId: u(),
    tenantId: u(),
    actorType,
    permissions: [],
    permissionVersion: 1,
    sessionId: undefined,
    jti: undefined,
    raw: {},
  });

  it('el personal con chat.reply recibe su ámbito', async () => {
    const repo = new FakeFileLibraryReadRepository();
    const id = u();
    repo.memberScope = [{ conversationId: id, lastActivityAtUtc: at(1) }];

    const response = await call(principal('TenantEmployee'), ['communication.chat.reply'], repo);

    expect(response.statusCode).toBe(200);
    expect(response.json()).toEqual({ conversationIds: [id], callIds: [], meetingIds: [], truncated: [] });
  });

  it('sin chat.reply responde 200 con las listas vacías', async () => {
    const repo = new FakeFileLibraryReadRepository();
    repo.memberScope = [{ conversationId: u(), lastActivityAtUtc: at(1) }];

    const response = await call(principal('TenantAdmin'), [], repo);

    expect(response.statusCode).toBe(200);
    expect(response.json()).toEqual({ conversationIds: [], callIds: [], meetingIds: [], truncated: [] });
  });

  it('el portal y PlatformAdmin reciben 403', async () => {
    expect((await call(principal('CustomerPortal'), ['communication.chat.reply'])).statusCode).toBe(403);
    expect((await call(principal('PlatformAdmin'), ['communication.chat.reply'])).statusCode).toBe(403);
  });
});
