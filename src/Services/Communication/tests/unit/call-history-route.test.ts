import Fastify, { type FastifyReply, type FastifyRequest } from 'fastify';
import { describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { Call } from '../../src/domain/calls/call.js';
import { registerCallRoutes } from '../../src/api/http/routes/calls.route.js';
import type { AppContainer } from '../../src/infrastructure/container.js';
import type { AuthenticatedPrincipal } from '../../src/infrastructure/jwks/jwt-verifier.js';
import type { CallHistoryFilter } from '../../src/application/ports/call-repository.js';

const TENANT_ID = randomUUID();
const USER_ID = randomUUID();
const OTHER_ID = randomUUID();

function principal(): AuthenticatedPrincipal {
  return { userId: USER_ID, tenantId: TENANT_ID, actorType: 'TenantEmployee', permissions: [], permissionVersion: 1, sessionId: undefined, jti: undefined, raw: {} };
}

function container(calls: Call[]) {
  const filters: (CallHistoryFilter | undefined)[] = [];
  const app = {
    userPermissions: {
      async findByUserId(userId: string) {
        return { userId, tenantId: TENANT_ID, permissions: ['communication.call.start'], permissionVersion: 1, roleIds: [], actorType: 'TenantEmployee', isActive: true, updatedAtUtc: new Date() };
      },
    },
    calls: {
      async listRecentForUser(input: { filter?: CallHistoryFilter }) {
        filters.push(input.filter);
        return calls.map((c) => c.toSnapshot());
      },
      async countRecentForUser() {
        return calls.length;
      },
      async findById(_t: string, id: string) {
        return calls.find((c) => c.id === id) ?? null;
      },
    },
    recordingSessions: { async listStatesByScopeIds() { return new Map(); } },
    effectiveSettings: { async resolve() { return { recording: { visibleInPortal: false } }; } },
  } as unknown as AppContainer;
  return { app, filters };
}

async function inject(c: AppContainer, url: string) {
  const app = Fastify();
  app.decorate('authenticate', async function authenticate(request: FastifyRequest, _reply: FastifyReply) {
    request.principal = principal();
  });
  await registerCallRoutes(app, c);
  await app.ready();
  const response = await app.inject({ method: 'GET', url });
  await app.close();
  return response;
}

function teamCall(caller: string, callee: string): Call {
  const created = Call.initiate({ tenantId: TENANT_ID, kind: 'Audio', caller: { userId: caller, displayName: 'A' }, callee: { userId: callee, displayName: 'B' } });
  if (!created.isSuccess) throw new Error('initiate');
  return created.value;
}

describe('GET /communication/calls (historial)', () => {
  it('lee audience y recorded de la URL y devuelve los campos nuevos', async () => {
    const { app, filters } = container([teamCall(USER_ID, OTHER_ID)]);

    const response = await inject(app, '/communication/calls?audience=team&recorded=true');

    expect(response.statusCode).toBe(200);
    expect(filters[0]).toEqual({ audience: 'team', recordedOnly: true });
    expect(response.json().items[0]).toMatchObject({ direction: 'outgoing', audience: 'team', counterpart: { userId: OTHER_ID } });
  });

  it('sin filtros sigue respondiendo como antes', async () => {
    const { app, filters } = container([]);

    const response = await inject(app, '/communication/calls');

    expect(response.statusCode).toBe(200);
    expect(filters[0]).toEqual({});
    expect(response.json()).toMatchObject({ items: [], page: 1, size: 20, totalCount: 0 });
  });

  it('lee la búsqueda por nombre (recortada) y la pasa junto a los demás filtros', async () => {
    const { app, filters } = container([]);

    const response = await inject(app, '/communication/calls?q=%20ana%20&audience=client');

    expect(response.statusCode).toBe(200);
    expect(filters[0]).toEqual({ audience: 'client', search: 'ana' });
  });

  it('una búsqueda vacía no filtra', async () => {
    const { app, filters } = container([]);

    await inject(app, '/communication/calls?q=%20%20');

    expect(filters[0]).toEqual({});
  });

  it('rechaza una audiencia desconocida', async () => {
    const { app } = container([]);

    const response = await inject(app, '/communication/calls?audience=everyone');

    expect(response.statusCode).toBe(400);
  });
});

describe('GET /communication/calls/:id', () => {
  it('devuelve la llamada al participante', async () => {
    const call = teamCall(OTHER_ID, USER_ID);
    const { app } = container([call]);

    const response = await inject(app, `/communication/calls/${call.id}`);

    expect(response.statusCode).toBe(200);
    expect(response.json()).toMatchObject({ id: call.id, direction: 'incoming' });
  });

  it('404 a quien no participo', async () => {
    const call = teamCall(OTHER_ID, randomUUID());
    const { app } = container([call]);

    const response = await inject(app, `/communication/calls/${call.id}`);

    expect(response.statusCode).toBe(404);
  });
});
