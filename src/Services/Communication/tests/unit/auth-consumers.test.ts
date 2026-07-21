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
    findByUserId: vi.fn(),
    markInactive: vi.fn(),
    findActiveByTenantAndRoleId: vi.fn(),
  };
  const userDirectory: UserDirectoryRepository = {
    upsert: vi.fn(),
    findByUserId: vi.fn(),
    markInactive: vi.fn(),
    searchByDisplayNameOrEmail: vi.fn(),
  };
  const rolePermissions: RolePermissionsProjectionRepository = {
    upsert: vi.fn(),
    findByRoleIds: vi.fn().mockResolvedValue([]),
  };
  const customerPortalAccounts: CustomerPortalAccountRepository = {
    upsert: vi.fn(),
    markInactiveByUserId: vi.fn(),
    findActiveByCustomerId: vi.fn(),
    findActiveByUserId: vi.fn(),
  };

  bindAuthConsumers(register, { userPermissions, userDirectory, rolePermissions, customerPortalAccounts });
  return { handlers, userPermissions, rolePermissions, customerPortalAccounts };
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
});

describe('bindAuthConsumers — auth.role.permissions_changed.v1 (Fase 2)', () => {
  it('cachea el rol y recomputa la union de permisos de un usuario con VARIOS roles', async () => {
    const { handlers, userPermissions, rolePermissions } = setup();

    // El usuario afectado tiene 2 roles — cambiar SOLO role-1 no debe pisarle
    // los permisos que le llegan de role-2 (esa es la razon de RolePermissionsProjection).
    vi.mocked(userPermissions.findActiveByTenantAndRoleId).mockResolvedValue([
      {
        userId: 'user-multi-role',
        tenantId: 'tenant-1',
        permissions: ['old.stale.permission'],
        permissionVersion: 5,
        roleIds: ['role-1', 'role-2'],
        actorType: 'TenantEmployee',
        isActive: true,
        updatedAtUtc: new Date(),
      },
    ]);
    vi.mocked(rolePermissions.findByRoleIds).mockResolvedValue([
      {
        roleId: 'role-1',
        tenantId: 'tenant-1',
        roleName: 'Employee',
        permissionCodes: ['cloudstorage.manage'],
        permissionsVersion: 3,
        updatedAtUtc: new Date(),
      },
      {
        roleId: 'role-2',
        tenantId: 'tenant-1',
        roleName: 'Preparer',
        permissionCodes: ['signature.request.create'],
        permissionsVersion: 1,
        updatedAtUtc: new Date(),
      },
    ]);

    await handlers.get('auth.role.permissions_changed.v1')!(
      envelope({
        RoleId: 'role-1',
        RoleName: 'Employee',
        PermissionCodes: ['cloudstorage.manage'],
        PermissionsVersion: 3,
      }),
    );

    expect(rolePermissions.upsert).toHaveBeenCalledWith(
      expect.objectContaining({
        roleId: 'role-1',
        roleName: 'Employee',
        permissionCodes: ['cloudstorage.manage'],
        permissionsVersion: 3,
      }),
    );
    expect(userPermissions.findActiveByTenantAndRoleId).toHaveBeenCalledWith('tenant-1', 'role-1');
    expect(userPermissions.upsert).toHaveBeenCalledWith(
      expect.objectContaining({
        userId: 'user-multi-role',
        // union de role-1 (cloudstorage.manage) + role-2 (signature.request.create) —
        // 'old.stale.permission' no aparece porque no viene de ningun rol vigente.
        permissions: expect.arrayContaining(['cloudstorage.manage', 'signature.request.create']),
        roleIds: ['role-1', 'role-2'],
      }),
    );
    const upsertCall = vi.mocked(userPermissions.upsert).mock.calls[0]?.[0];
    expect(upsertCall?.permissions).toHaveLength(2);
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
