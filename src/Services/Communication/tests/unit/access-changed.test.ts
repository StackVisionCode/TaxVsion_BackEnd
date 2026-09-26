import { describe, expect, it, vi } from 'vitest';
import { bindAuthConsumers } from '../../src/application/event-handlers/auth-consumers.js';
import { bindSubscriptionConsumers } from '../../src/application/event-handlers/subscription-consumers.js';
import type { IncomingEnvelope } from '../../src/application/ports/event-consumer.js';

/**
 * A5 — `access.changed`. Sin esto, cuando a alguien le cambian los permisos o la oficina cambia de
 * plan, el CRM se queda con el sidebar y los botones de antes hasta que alguien recargue: el 403 llega
 * despues, al hacer clic. El payload no lleva permisos ni nada del plan a proposito — solo dice "volve
 * a pedir `GET /auth/me/access`".
 */
function fakeEmitter() {
  return {
    emitToConversation: vi.fn(),
    emitToCall: vi.fn(),
    emitToMeeting: vi.fn(),
    emitToUser: vi.fn(),
    emitToTenant: vi.fn(),
    emitToTenantStaff: vi.fn(),
    emitToTenantMembers: vi.fn(),
  };
}

function envelope(eventType: string, payload: Record<string, unknown>): IncomingEnvelope {
  return {
    eventId: 'evt-1',
    eventType,
    tenantId: 'tenant-1',
    correlationId: 'corr-1',
    occurredOnUtc: new Date().toISOString(),
    payload,
  };
}

describe('access.changed — cambio de permisos de un usuario', () => {
  function setup() {
    const handlers = new Map<string, (env: IncomingEnvelope) => Promise<void>>();
    const emitter = fakeEmitter();
    bindAuthConsumers((eventType, handler) => void handlers.set(eventType, handler), {
      userPermissions: {
        upsert: vi.fn(),
        upsertIdentityPreservingPermissions: vi.fn(),
        findByUserId: vi.fn(),
        markInactive: vi.fn(),
        markActive: vi.fn(),
        findActiveByTenantAndRoleId: vi.fn().mockResolvedValue([]),
      },
      userDirectory: {
        upsert: vi.fn(),
        findByUserId: vi.fn(),
        markInactive: vi.fn(),
        markActive: vi.fn(),
        searchByDisplayNameOrEmail: vi.fn(),
      },
      rolePermissions: { upsert: vi.fn(), findByRoleIds: vi.fn().mockResolvedValue([]) },
      customerPortalAccounts: {
        upsert: vi.fn(),
        markInactiveByUserId: vi.fn(),
        markActiveByUserId: vi.fn(),
        findActiveByCustomerId: vi.fn(),
        findActiveByCustomerIds: vi.fn().mockResolvedValue([]),
        findActiveByUserId: vi.fn(),
      },
      emitter,
    });
    return { handlers, emitter };
  }

  it('avisa solo al usuario, con su perm_v nuevo', async () => {
    const { handlers, emitter } = setup();

    await handlers.get('auth.user.roles_changed.v1')!(
      envelope('auth.user.roles_changed.v1', {
        UserId: 'user-1',
        PermissionsVersion: 7,
        PermissionCodes: ['customers.view'],
        ActorType: 'TenantEmployee',
      }),
    );

    expect(emitter.emitToUser).toHaveBeenCalledTimes(1);
    const call = emitter.emitToUser.mock.calls[0]![0] as {
      userId: string;
      event: string;
      envelope: { payload: { scope: string; permissionsVersion: number | null } };
    };
    expect(call.userId).toBe('user-1');
    expect(call.event).toBe('access.changed');
    expect(call.envelope.payload).toEqual({ scope: 'user', permissionsVersion: 7 });
    // Nunca al tenant: el cambio es de una sola persona.
    expect(emitter.emitToTenant).not.toHaveBeenCalled();
    expect(emitter.emitToTenantMembers).not.toHaveBeenCalled();
  });

  it('no avisa si el evento no trae userId', async () => {
    const { handlers, emitter } = setup();

    await handlers.get('auth.user.roles_changed.v1')!(
      envelope('auth.user.roles_changed.v1', { PermissionsVersion: 7 }),
    );

    expect(emitter.emitToUser).not.toHaveBeenCalled();
  });
});

describe('access.changed — cambio de entitlements del tenant', () => {
  function setup() {
    const handlers = new Map<string, (env: IncomingEnvelope) => Promise<void>>();
    const emitter = fakeEmitter();
    bindSubscriptionConsumers((eventType, handler) => void handlers.set(eventType, handler), {
      limits: { get: vi.fn(), upsert: vi.fn() } as never,
      emitter,
    });
    return { handlers, emitter };
  }

  it('avisa a los miembros autenticados, nunca al room del tenant (ahi hay invitados)', async () => {
    const { handlers, emitter } = setup();

    await handlers.get('subscription.entitlements_changed.v1')!(
      envelope('subscription.entitlements_changed.v1', {
        planCode: 'pro',
        entitlementValues: { 'module.comms': 'true', 'module.customers': 'true' },
      }),
    );

    expect(emitter.emitToTenantMembers).toHaveBeenCalledTimes(1);
    const call = emitter.emitToTenantMembers.mock.calls[0]![0] as {
      tenantId: string;
      event: string;
      envelope: { payload: { scope: string; permissionsVersion: number | null } };
    };
    expect(call.tenantId).toBe('tenant-1');
    expect(call.event).toBe('access.changed');
    expect(call.envelope.payload).toEqual({ scope: 'tenant', permissionsVersion: null });
    expect(emitter.emitToTenant).not.toHaveBeenCalled();
  });

  it('un evento sin planCode no dispara nada', async () => {
    const { handlers, emitter } = setup();

    await handlers.get('subscription.entitlements_changed.v1')!(
      envelope('subscription.entitlements_changed.v1', { entitlementValues: {} }),
    );

    expect(emitter.emitToTenantMembers).not.toHaveBeenCalled();
  });
});
