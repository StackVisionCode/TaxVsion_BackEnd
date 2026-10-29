import Fastify, { type FastifyReply, type FastifyRequest } from 'fastify';
import { describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { registerDirectoryRoutes } from '../../src/api/http/routes/directory.route.js';
import type { AppContainer } from '../../src/infrastructure/container.js';
import type { AuthenticatedPrincipal } from '../../src/infrastructure/jwks/jwt-verifier.js';
import type { CustomerDirectoryEntrySnapshot } from '../../src/application/ports/customer-directory-repository.js';

/**
 * El término del autocomplete de clientes suele ser un email o un nombre: en la URL acababa en los
 * logs del Gateway y del servicio. `POST /communication/directory/customers/search` lo lleva en el
 * cuerpo con la misma puerta y la misma respuesta; el GET queda solo por compatibilidad.
 */

function u(): string {
  return randomUUID();
}

const TENANT_ID = u();
const CUSTOMER_ID = u();
const PORTAL_USER_ID = u();

function principal(actorType: string, userId = u()): AuthenticatedPrincipal {
  return {
    userId,
    tenantId: TENANT_ID,
    actorType,
    permissions: [],
    permissionVersion: 1,
    sessionId: undefined,
    jti: undefined,
    raw: {},
  };
}

const ENTRY: CustomerDirectoryEntrySnapshot = {
  customerId: CUSTOMER_ID,
  tenantId: TENANT_ID,
  displayName: 'Mina Torres',
  email: 'mina@example.com',
  isActive: true,
} as unknown as CustomerDirectoryEntrySnapshot;

interface Recorder {
  searches: { tenantId: string; term: string; limit: number }[];
}

function fakeContainer(
  recorder: Recorder,
  permissionsByUserId: Record<string, readonly string[]>,
): AppContainer {
  return {
    assignmentVisibilityEnabled: false,
    settings: {
      async get(tenantId: string) {
        return {
          tenantId,
          chatEnabled: true,
          employeeToEmployeeChatEnabled: true,
          restrictCustomerChatToAssignedPreparer: false,
          screenshotsEnabled: true,
          internalGroupsEnabled: true,
          messageRetentionDays: 365,
        };
      },
    },
    userPermissions: {
      async findByUserId(userId: string) {
        const permissions = permissionsByUserId[userId];
        if (!permissions) return null;
        return {
          userId,
          tenantId: TENANT_ID,
          permissions,
          permissionVersion: 1,
          roleIds: [],
          actorType: 'TenantEmployee',
          isActive: true,
          updatedAtUtc: new Date(),
        };
      },
    },
    customerAssignments: {
      async getAssignedCustomerIds() {
        return [];
      },
    },
    customerDirectory: {
      async searchByDisplayNameOrEmail(tenantId: string, term: string, limit: number) {
        recorder.searches.push({ tenantId, term, limit });
        return [ENTRY];
      },
    },
    customerPortalAccounts: {
      async findActiveByCustomerIds(customerIds: readonly string[]) {
        return customerIds.includes(CUSTOMER_ID)
          ? [{ customerId: CUSTOMER_ID, tenantId: TENANT_ID, userId: PORTAL_USER_ID, isActive: true }]
          : [];
      },
    },
  } as unknown as AppContainer;
}

async function call(
  actor: AuthenticatedPrincipal,
  request: { method: 'GET' | 'POST'; url: string; payload?: unknown },
  options: { granted?: boolean } = {},
) {
  const recorder: Recorder = { searches: [] };
  const permissions = options.granted === false ? [] : ['communication.chat.start'];
  const container = fakeContainer(recorder, { [actor.userId]: permissions });
  const app = Fastify();
  app.decorate('authenticate', async function authenticate(req: FastifyRequest, _reply: FastifyReply) {
    req.principal = actor;
  });
  await registerDirectoryRoutes(app, container);
  await app.ready();
  const response = await app.inject({
    method: request.method,
    url: request.url,
    ...(request.payload !== undefined ? { payload: request.payload as Record<string, unknown> } : {}),
  });
  await app.close();
  return { response, recorder };
}

const SEARCH_URL = '/communication/directory/customers/search';

describe('POST /communication/directory/customers/search', () => {
  it('busca con el término del cuerpo y devuelve la misma forma que el GET', async () => {
    const actor = principal('TenantEmployee');
    const post = await call(actor, {
      method: 'POST',
      url: SEARCH_URL,
      payload: { q: 'mina@example.com', limit: 7 },
    });
    const get = await call(actor, {
      method: 'GET',
      url: '/communication/directory/customers?q=mina%40example.com&limit=7',
    });

    expect(post.response.statusCode).toBe(200);
    expect(post.recorder.searches).toEqual([{ tenantId: TENANT_ID, term: 'mina@example.com', limit: 7 }]);
    expect(post.response.json()).toEqual([{ ...ENTRY, portalUserId: PORTAL_USER_ID }]);
    expect(post.response.json()).toEqual(get.response.json());
  });

  it('usa el límite por defecto cuando el cuerpo no lo trae', async () => {
    const { response, recorder } = await call(principal('TenantAdmin'), {
      method: 'POST',
      url: SEARCH_URL,
      payload: { q: 'mina' },
    });

    expect(response.statusCode).toBe(200);
    expect(recorder.searches[0]?.limit).toBe(10);
  });

  it('rechaza a un cliente del portal con 403 sin buscar', async () => {
    const { response, recorder } = await call(principal('CustomerPortal'), {
      method: 'POST',
      url: SEARCH_URL,
      payload: { q: 'mina' },
    });

    expect(response.statusCode).toBe(403);
    expect(response.json()).toMatchObject({ code: 'Auth.Forbidden' });
    expect(recorder.searches).toHaveLength(0);
  });

  it('exige el permiso de iniciar chat igual que el GET', async () => {
    const { response, recorder } = await call(
      principal('TenantEmployee'),
      { method: 'POST', url: SEARCH_URL, payload: { q: 'mina' } },
      { granted: false },
    );

    expect(response.statusCode).toBe(403);
    expect(recorder.searches).toHaveLength(0);
  });

  it.each([
    ['sin cuerpo', undefined],
    ['término vacío', { q: '' }],
    ['término de más de 100 caracteres', { q: 'x'.repeat(101) }],
    ['límite fuera de rango', { q: 'mina', limit: 26 }],
  ])('responde 400 sin eco del valor: %s', async (_case, payload) => {
    const { response, recorder } = await call(principal('TenantEmployee'), {
      method: 'POST',
      url: SEARCH_URL,
      payload,
    });

    expect(response.statusCode).toBe(400);
    expect(response.json()).toMatchObject({ code: 'Directory.InvalidSearch' });
    expect(response.body).not.toContain('x'.repeat(101));
    expect(recorder.searches).toHaveLength(0);
  });
});

describe('logs de petición', () => {
  it('el log por petición de Fastify no incluye el término del cuerpo', async () => {
    const lines: string[] = [];
    const actor = principal('TenantEmployee');
    const container = fakeContainer({ searches: [] }, { [actor.userId]: ['communication.chat.start'] });
    // Como build-server.ts: logging de peticiones activo (por defecto) y serializers por defecto.
    const app = Fastify({
      logger: { level: 'trace', stream: { write: (line: string) => void lines.push(line) } },
    });
    app.decorate('authenticate', async function authenticate(req: FastifyRequest, _reply: FastifyReply) {
      req.principal = actor;
    });
    await registerDirectoryRoutes(app, container);
    await app.ready();
    const response = await app.inject({
      method: 'POST',
      url: SEARCH_URL,
      payload: { q: 'secret.term@example.com' },
    });
    await app.close();

    expect(response.statusCode).toBe(200);
    const logged = lines.join('\n');
    expect(logged).toContain(SEARCH_URL);
    expect(logged).not.toContain('secret.term');
  });
});

describe('GET /communication/directory/customers (compatibilidad)', () => {
  it('sigue respondiendo con q en la URL', async () => {
    const { response, recorder } = await call(principal('TenantEmployee'), {
      method: 'GET',
      url: '/communication/directory/customers?q=mina',
    });

    expect(response.statusCode).toBe(200);
    expect(recorder.searches[0]?.term).toBe('mina');
  });

  it('responde 400 cuando falta q', async () => {
    const { response } = await call(principal('TenantEmployee'), {
      method: 'GET',
      url: '/communication/directory/customers',
    });

    expect(response.statusCode).toBe(400);
    expect(response.json()).toMatchObject({ code: 'Directory.InvalidSearch' });
  });
});
