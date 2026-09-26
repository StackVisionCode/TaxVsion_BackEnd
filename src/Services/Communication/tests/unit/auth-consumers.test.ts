import { describe, expect, it, vi } from 'vitest';
import { bindAuthConsumers } from '../../src/application/event-handlers/auth-consumers.js';
import type { UserPermissionsProjectionRepository } from '../../src/application/ports/user-permissions-projection-repository.js';
import type { UserDirectoryRepository } from '../../src/application/ports/user-directory-repository.js';
import type { RolePermissionsProjectionRepository } from '../../src/application/ports/role-permissions-projection-repository.js';
import type { CustomerPortalAccountRepository } from '../../src/application/ports/customer-portal-account-repository.js';
import type { IncomingEnvelope } from '../../src/application/ports/event-consumer.js';

/**
 * Test de contrato (regla de la Fase 0 del plan de notificaciones): el payload de
 * abajo usa los nombres de campo EXACTOS que UserRolesChangedIntegrationEvent.cs
 * serializa hoy (PascalCase, `PermissionCodes` + `PermissionsVersion` con "s") —
 * copiados literalmente del record de C#, no adivinados.
 *
 * Bug original (Fase 1): este handler leia `permissions`/`Permissions` (campo que
 * Auth nunca envio) y `permissionVersion` (el real es `PermissionsVersion`, con
 * "s") — la proyeccion `UserPermissionsProjection` quedaba siempre con
 * `permissions: []` en silencio, sin ningun error visible.
 */
function setup() {
  const handlers = new Map<string, (env: IncomingEnvelope) => Promise<void>>();
  const register = (eventType: string, handler: (env: IncomingEnvelope) => Promise<void>) => {
    handlers.set(eventType, handler);
  };
  const userPermissions: UserPermissionsProjectionRepository = {
    upsert: vi.fn(),
    upsertIdentityPreservingPermissions: vi.fn(),
    findByUserId: vi.fn(),
    markInactive: vi.fn(),
    markActive: vi.fn(),
    findActiveByTenantAndRoleId: vi.fn(),
  };
  const userDirectory: UserDirectoryRepository = {
    upsert: vi.fn(),
    findByUserId: vi.fn(),
    markInactive: vi.fn(),
    markActive: vi.fn(),
    searchByDisplayNameOrEmail: vi.fn(),
  };
  const rolePermissions: RolePermissionsProjectionRepository = {
    upsert: vi.fn(),
    findByRoleIds: vi.fn().mockResolvedValue([]),
  };
  const customerPortalAccounts: CustomerPortalAccountRepository = {
    upsert: vi.fn(),
    markInactiveByUserId: vi.fn(),
    markActiveByUserId: vi.fn(),
    findActiveByCustomerId: vi.fn(),
    findActiveByCustomerIds: vi.fn().mockResolvedValue([]),
    findActiveByUserId: vi.fn(),
  };

  const emitter = {
    emitToConversation: vi.fn(),
    emitToCall: vi.fn(),
    emitToMeeting: vi.fn(),
    emitToUser: vi.fn(),
    emitToTenant: vi.fn(),
    emitToTenantStaff: vi.fn(),
    emitToTenantMembers: vi.fn(),
  };

  bindAuthConsumers(register, {
    userPermissions,
    userDirectory,
    rolePermissions,
    customerPortalAccounts,
    emitter,
  });
  return { handlers, userPermissions, userDirectory, rolePermissions, customerPortalAccounts, emitter };
}

function envelope(payload: Record<string, unknown>): IncomingEnvelope {
  return {
    eventId: 'evt-1',
    eventType: 'auth.user.roles_changed.v1',
    tenantId: 'tenant-1',
    correlationId: 'corr-1',
    occurredOnUtc: new Date().toISOString(),
    payload,
  };
}

describe('bindAuthConsumers — contrato de campos con Auth (.NET)', () => {
  it('auth.user.roles_changed.v1 puebla la proyeccion con PermissionCodes y PermissionsVersion reales', async () => {
    const { handlers, userPermissions } = setup();

    await handlers.get('auth.user.roles_changed.v1')!(
      envelope({
        UserId: 'user-1',
        PermissionsVersion: 3,
        RoleNames: ['Employee'],
        PermissionCodes: ['notification.email.send', 'cloudstorage.manage'],
      }),
    );

    expect(userPermissions.upsert).toHaveBeenCalledTimes(1);
    expect(userPermissions.upsert).toHaveBeenCalledWith(
      expect.objectContaining({
        userId: 'user-1',
        permissions: ['notification.email.send', 'cloudstorage.manage'],
        permissionVersion: 3,
      }),
    );
  });

  it('no queda con permissions vacio cuando PermissionsVersion viene sin RoleNames (regresion del bug original)', async () => {
    const { handlers, userPermissions } = setup();

    await handlers.get('auth.user.roles_changed.v1')!(
      envelope({
        UserId: 'user-2',
        PermissionsVersion: 1,
        RoleNames: [],
        PermissionCodes: ['signature.request.create'],
      }),
    );

    expect(userPermissions.upsert).toHaveBeenCalledWith(
      expect.objectContaining({ permissions: ['signature.request.create'], permissionVersion: 1 }),
    );
  });

  it('auth.user.roles_changed.v1 (Fase 2) puebla RoleIds desde el campo real del evento', async () => {
    const { handlers, userPermissions } = setup();

    await handlers.get('auth.user.roles_changed.v1')!(
      envelope({
        UserId: 'user-3',
        PermissionsVersion: 2,
        RoleNames: ['Employee'],
        RoleIds: ['11111111-1111-1111-1111-111111111111'],
        PermissionCodes: ['cloudstorage.manage'],
      }),
    );

    expect(userPermissions.upsert).toHaveBeenCalledWith(
      expect.objectContaining({ roleIds: ['11111111-1111-1111-1111-111111111111'] }),
    );
  });

  it('auth.user.registered.v1 NO pisa los permisos: el evento no los transporta', async () => {
    const { handlers, userPermissions } = setup();

    const registered = handlers.get('auth.user.registered.v1');
    expect(registered).toBeDefined();
    await registered?.({
      ...envelope({ userId: 'user-4', email: 'nuevo@example.com', actorType: 'TenantAdmin' }),
      eventType: 'auth.user.registered.v1',
    });

    // Si volviera a llamar `upsert`, escribiria permissions: [] tambien en el UPDATE y borraria
    // los permisos que `roles_changed` ya hubiera dejado (el consumer no garantiza orden).
    expect(userPermissions.upsert).not.toHaveBeenCalled();
    expect(userPermissions.upsertIdentityPreservingPermissions).toHaveBeenCalledWith(
      expect.objectContaining({ userId: 'user-4', tenantId: 'tenant-1', actorType: 'TenantAdmin' }),
    );
  });

  it('auth.user.reactivated.v1 re-activa las tres proyecciones (contraparte de deactivated)', async () => {
    const { handlers, userPermissions, userDirectory, customerPortalAccounts } = setup();

    const reactivated = handlers.get('auth.user.reactivated.v1');
    // Sin este handler, un portal reactivado en Auth quedaba "activo pero no chateable" en Communication.
    expect(reactivated).toBeDefined();
    await reactivated?.({
      ...envelope({ UserId: 'user-portal-1', Email: 'cliente@example.com', ActorType: 'CustomerPortal' }),
      eventType: 'auth.user.reactivated.v1',
    });

    expect(userPermissions.markActive).toHaveBeenCalledWith('user-portal-1', expect.any(Date));
    expect(userDirectory.markActive).toHaveBeenCalledWith('user-portal-1');
    expect(customerPortalAccounts.markActiveByUserId).toHaveBeenCalledWith('user-portal-1');
  });

  it('auth.user.reactivated.v1 sin userId no toca ninguna proyeccion', async () => {
    const { handlers, userPermissions, userDirectory, customerPortalAccounts } = setup();

    await handlers.get('auth.user.reactivated.v1')!({
      ...envelope({ Email: 'sin-id@example.com', ActorType: 'CustomerPortal' }),
      eventType: 'auth.user.reactivated.v1',
    });

    expect(userPermissions.markActive).not.toHaveBeenCalled();
    expect(userDirectory.markActive).not.toHaveBeenCalled();
    expect(customerPortalAccounts.markActiveByUserId).not.toHaveBeenCalled();
  });
});

describe('bindAuthConsumers — auth.role.permissions_changed.v1 (Fase 2)', () => {
  /**
   * A5 — contrato nuevo, el mismo que ya rige en los 24 consumers equivalentes de .NET desde A2: este
   * handler cachea rol -> permisos y NO recompone la union del usuario.
   *
   * Recomponerla resucitaba los denies. La capa de denies vive solo en Auth, asi que una union armada
   * con los permisos cacheados de los roles le devolvia al usuario justo el permiso que un
   * administrador le habia quitado. Ahora Auth publica `UserRolesChangedIntegrationEvent` por cada
   * titular con sus codigos ya efectivos.
   */
  it('cachea el rol y NO recompone la union del usuario (los denies viven en Auth)', async () => {
    const { handlers, userPermissions, rolePermissions } = setup();

    vi.mocked(userPermissions.findActiveByTenantAndRoleId).mockResolvedValue([
      {
        userId: 'user-multi-role',
        tenantId: 'tenant-1',
        // Este set incluye un permiso denegado por un administrador. Si el handler recompusiera la
        // union a partir de los roles, se lo devolveria.
        permissions: ['cloudstorage.manage'],
        permissionVersion: 5,
        roleIds: ['role-1', 'role-2'],
        actorType: 'TenantEmployee',
        isActive: true,
        updatedAtUtc: new Date(),
      },
    ]);

    await handlers.get('auth.role.permissions_changed.v1')!(
      envelope({
        RoleId: 'role-1',
        RoleName: 'Employee',
        PermissionCodes: ['cloudstorage.manage', 'signature.request.create'],
        PermissionsVersion: 3,
      }),
    );

    expect(rolePermissions.upsert).toHaveBeenCalledWith(
      expect.objectContaining({
        roleId: 'role-1',
        roleName: 'Employee',
        permissionCodes: ['cloudstorage.manage', 'signature.request.create'],
        permissionsVersion: 3,
      }),
    );
    expect(userPermissions.upsert).not.toHaveBeenCalled();
  });

  it('avisa a cada titular conectado que su acceso cambio', async () => {
    const { handlers, userPermissions, emitter } = setup();

    vi.mocked(userPermissions.findActiveByTenantAndRoleId).mockResolvedValue([
      {
        userId: 'user-1',
        tenantId: 'tenant-1',
        permissions: [],
        permissionVersion: 5,
        roleIds: ['role-1'],
        actorType: 'TenantEmployee',
        isActive: true,
        updatedAtUtc: new Date(),
      },
    ]);

    await handlers.get('auth.role.permissions_changed.v1')!(
      envelope({ RoleId: 'role-1', RoleName: 'Employee', PermissionCodes: [], PermissionsVersion: 3 }),
    );

    expect(emitter.emitToUser).toHaveBeenCalledWith(
      expect.objectContaining({
        tenantId: 'tenant-1',
        userId: 'user-1',
        event: 'access.changed',
      }),
    );
  });

  it('no hace nada si no hay usuarios activos con ese RoleId', async () => {
    const { handlers, userPermissions, rolePermissions } = setup();
    vi.mocked(userPermissions.findActiveByTenantAndRoleId).mockResolvedValue([]);

    await handlers.get('auth.role.permissions_changed.v1')!(
      envelope({ RoleId: 'role-orphan', RoleName: 'Unused', PermissionCodes: [], PermissionsVersion: 1 }),
    );

    expect(rolePermissions.upsert).toHaveBeenCalledTimes(1); // el cache del rol siempre se actualiza
    expect(userPermissions.upsert).not.toHaveBeenCalled();
  });
});
