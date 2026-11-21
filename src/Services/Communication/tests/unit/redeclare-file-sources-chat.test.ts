import Fastify, { type FastifyReply, type FastifyRequest } from 'fastify';
import { describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { redeclareFileSources } from '../../src/application/use-cases/redeclare-file-sources.js';
import { registerAdminSettingsRoutes } from '../../src/api/http/routes/admin-settings.route.js';
import type { FileSourceDeclaredEvent } from '../../src/contracts/events/cloudstorage-events.js';
import type { IntegrationEvent } from '../../src/contracts/events/integration-event.js';
import type { AppContainer } from '../../src/infrastructure/container.js';
import type { AuthenticatedPrincipal } from '../../src/infrastructure/jwks/jwt-verifier.js';
import {
  createFakeProjectionRepository,
  fakeSnapshot,
} from '../helpers/fake-user-permissions-projection-repository.js';
import { FakeFileLibraryReadRepository } from '../helpers/chat-file-library.js';

const u = () => randomUUID();

function seeded(count: number) {
  const tenantId = u();
  const repo = new FakeFileLibraryReadRepository();
  const conversationId = u();
  const customerId = u();
  for (let i = 0; i < count; i++) {
    repo.redeclare.push({ tenantId, fileId: u(), conversationId, isVoiceNote: i === 0 });
  }
  // Otro tenant: nunca se recorre.
  repo.redeclare.push({ tenantId: u(), fileId: u(), conversationId: u(), isVoiceNote: false });
  repo.clients.push({ tenantId, conversationId, actorType: 'CustomerPortal', customerId, isRemoved: false });
  const published: IntegrationEvent[] = [];
  const deps = {
    fileLibrary: repo,
    publisher: { enqueue: async (e: IntegrationEvent) => void published.push(e) },
  };
  return { tenantId, repo, conversationId, customerId, published, deps };
}

describe('redeclareFileSources — kind=chat', () => {
  it('dryRun cuenta sin encolar', async () => {
    const s = seeded(3);

    const result = await redeclareFileSources(
      { tenantId: s.tenantId, kind: 'chat', dryRun: true, correlationId: 'c' },
      s.deps,
    );

    expect(result).toEqual({ dryRun: true, scanned: 3, declared: 0 });
    expect(s.published).toEqual([]);
  });

  it('real encola una declaración por tracking, con su papel y el cliente de la conversación', async () => {
    const s = seeded(3);

    const result = await redeclareFileSources(
      { tenantId: s.tenantId, kind: 'chat', dryRun: false, correlationId: 'c' },
      s.deps,
    );

    expect(result).toEqual({ dryRun: false, scanned: 3, declared: 3 });
    const events = s.published as FileSourceDeclaredEvent[];
    expect(new Set(events.map((e) => e.fileId))).toEqual(
      new Set(s.repo.redeclare.filter((r) => r.tenantId === s.tenantId).map((r) => r.fileId)),
    );
    expect(events.map((e) => e.sourceRole).sort()).toEqual(['attachment', 'attachment', 'voice_note']);
    expect(
      events.every(
        (e) =>
          e.sourceResourceId === s.conversationId &&
          e.relatedCustomerId === s.customerId &&
          e.tenantId === s.tenantId,
      ),
    ).toBe(true);
  });

  it('recorre por tandas y repetirlo encola lo mismo', async () => {
    const s = seeded(1_203);

    const first = await redeclareFileSources(
      { tenantId: s.tenantId, kind: 'chat', dryRun: false, correlationId: 'c' },
      s.deps,
    );
    const firstIds = s.published.map((e) => (e as FileSourceDeclaredEvent).fileId);
    s.published.length = 0;
    const second = await redeclareFileSources(
      { tenantId: s.tenantId, kind: 'chat', dryRun: false, correlationId: 'c' },
      s.deps,
    );

    expect(first).toEqual({ dryRun: false, scanned: 1_203, declared: 1_203 });
    expect(second).toEqual(first);
    expect(s.published.map((e) => (e as FileSourceDeclaredEvent).fileId)).toEqual(firstIds);
  });
});

describe('POST /communication/admin/files/redeclare-sources', () => {
  async function post(actorType: string, url: string) {
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
    const repo = new FakeFileLibraryReadRepository();
    const published: IntegrationEvent[] = [];
    const container = {
      userPermissions: createFakeProjectionRepository([
        fakeSnapshot({ userId: principal.userId, permissions: ['communication.platform.manage'], actorType }),
      ]),
      fileLibrary: repo,
      publisher: { enqueue: async (e: IntegrationEvent) => void published.push(e) },
    } as unknown as AppContainer;
    const app = Fastify();
    app.decorate('authenticate', async (request: FastifyRequest, _reply: FastifyReply) => {
      request.principal = principal;
    });
    await registerAdminSettingsRoutes(app, container);
    await app.ready();
    const response = await app.inject({ method: 'POST', url });
    await app.close();
    return response;
  }

  it('PlatformAdmin con dryRun por defecto', async () => {
    const response = await post(
      'PlatformAdmin',
      `/communication/admin/files/redeclare-sources?tenantId=${u()}&kind=chat`,
    );

    expect(response.statusCode).toBe(200);
    expect(response.json()).toEqual({ dryRun: true, scanned: 0, declared: 0 });
  });

  it('solo PlatformAdmin; kind desconocido o sin tenant → 400', async () => {
    expect(
      (await post('TenantAdmin', `/communication/admin/files/redeclare-sources?tenantId=${u()}&kind=chat`))
        .statusCode,
    ).toBe(403);
    expect(
      (await post('PlatformAdmin', `/communication/admin/files/redeclare-sources?tenantId=${u()}&kind=other`))
        .statusCode,
    ).toBe(400);
    expect(
      (await post('PlatformAdmin', '/communication/admin/files/redeclare-sources?kind=chat')).statusCode,
    ).toBe(400);
  });
});
