import Fastify, { type FastifyReply, type FastifyRequest } from 'fastify';
import { describe, expect, it } from 'vitest';
import { randomUUID } from 'node:crypto';
import { registerCallRoutes } from '../../src/api/http/routes/calls.route.js';
import type { AppContainer } from '../../src/infrastructure/container.js';
import type { AuthenticatedPrincipal } from '../../src/infrastructure/jwks/jwt-verifier.js';

/**
 * `GET /communication/customers/:customerId/calls` es una pantalla del CRM (perfil del cliente →
 * Activity → Call history) y estaba solo detras de `authenticate`: un cliente del portal podia pedir
 * el customerId de OTRO cliente y leerle el historial completo de llamadas.
 */

function u(): string {
  return randomUUID();
}

const TENANT_ID = u();
const CUSTOMER_ID = u();
const PORTAL_USER_ID = u();

function principal(actorType: string, userId: string): AuthenticatedPrincipal {
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

function fakeContainer(overrides: {
  assignmentVisibilityEnabled?: boolean;
  assignedUserIds?: readonly string[];
  permissionsByUserId?: Record<string, readonly string[]>;
}): AppContainer {
  const assigned = overrides.assignedUserIds ?? [];
  return {
    assignmentVisibilityEnabled: overrides.assignmentVisibilityEnabled ?? false,
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
        const permissions = overrides.permissionsByUserId?.[userId];
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
      async isAssigned(_tenantId: string, _customerId: string, userId: string) {
        return assigned.includes(userId);
      },
    },
    customerPortalAccounts: {
      async findActiveByCustomerId(customerId: string) {
        return customerId === CUSTOMER_ID
          ? { customerId, tenantId: TENANT_ID, userId: PORTAL_USER_ID, isActive: true }
          : null;
      },
    },
    calls: {
      async listRecentForUser() {
        return [];
      },
    },
  } as unknown as AppContainer;
}

async function buildApp(container: AppContainer, actor: AuthenticatedPrincipal) {
  const app = Fastify();
  app.decorate('authenticate', async function authenticate(request: FastifyRequest, _reply: FastifyReply) {
    request.principal = actor;
  });
  await registerCallRoutes(app, container);
  await app.ready();
  return app;
}

async function get(container: AppContainer, actor: AuthenticatedPrincipal, customerId = CUSTOMER_ID) {
  const app = await buildApp(container, actor);
  const response = await app.inject({ method: 'GET', url: `/communication/customers/${customerId}/calls` });
  await app.close();
  return response;
}

describe('GET /communication/customers/:customerId/calls', () => {
  it('rechaza a un cliente del portal que pide el historial de otro cliente', async () => {
    const response = await get(fakeContainer({}), principal('CustomerPortal', u()));

    expect(response.statusCode).toBe(403);
    expect(response.json()).toMatchObject({ code: 'Auth.Forbidden' });
  });

  it('rechaza al cliente incluso cuando pide su propio customerId', async () => {
    const response = await get(fakeContainer({}), principal('CustomerPortal', PORTAL_USER_ID));

    expect(response.statusCode).toBe(403);
  });

  it('rechaza a un invitado de meeting', async () => {
    const response = await get(fakeContainer({}), principal('Guest', u()));

    expect(response.statusCode).toBe(403);
  });

  it('deja pasar al personal de la oficina con la visibilidad por asignacion apagada', async () => {
    const response = await get(fakeContainer({}), principal('TenantEmployee', u()));

    expect(response.statusCode).toBe(200);
    expect(response.json()).toMatchObject({ hasPortalAccount: true });
  });

  it('con la visibilidad por asignacion encendida, rechaza al empleado no asignado', async () => {
    const employeeUserId = u();
    const response = await get(
      fakeContainer({ assignmentVisibilityEnabled: true, assignedUserIds: [] }),
      principal('TenantEmployee', employeeUserId),
    );

    expect(response.statusCode).toBe(403);
  });

  it('con la visibilidad por asignacion encendida, deja pasar al empleado asignado', async () => {
    const employeeUserId = u();
    const response = await get(
      fakeContainer({ assignmentVisibilityEnabled: true, assignedUserIds: [employeeUserId] }),
      principal('TenantEmployee', employeeUserId),
    );

    expect(response.statusCode).toBe(200);
  });

  it('con la visibilidad encendida, quien ve a todos los clientes no necesita asignacion', async () => {
    const adminUserId = u();
    const response = await get(
      fakeContainer({
        assignmentVisibilityEnabled: true,
        assignedUserIds: [],
        permissionsByUserId: { [adminUserId]: ['customers.view_all'] },
      }),
      principal('TenantAdmin', adminUserId),
    );

    expect(response.statusCode).toBe(200);
  });
});
