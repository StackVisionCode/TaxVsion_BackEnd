import type { UserPermissionsProjectionRepository } from '../ports/user-permissions-projection-repository.js';
import type { UserDirectoryRepository } from '../ports/user-directory-repository.js';
import type { RolePermissionsProjectionRepository } from '../ports/role-permissions-projection-repository.js';
import type { CustomerPortalAccountRepository } from '../ports/customer-portal-account-repository.js';
import type { IncomingEnvelope } from '../ports/event-consumer.js';

/**
 * Auth consumers — mantienen al dia cuatro proyecciones locales:
 *   - `UserPermissionsProjection`: autorizacion fuera del request/socket.
 *   - `UserDirectoryEntry`: displayName real para payloads de chat/call/meeting
 *     (cierra el placeholder userId-como-nombre documentado en chat-handlers.ts).
 *   - `RolePermissionsProjection` (Fase 2): cache de soporte para recomputar la union de
 *     permisos de un usuario multi-rol cuando cambia UN rol — ver el handler de
 *     `auth.role.permissions_changed.v1` mas abajo.
 *   - `CustomerPortalAccount` (Fase 6, notificaciones dinamicas): (CustomerId -> UserId de
 *     portal activo), para poder notificar a un cliente cuando firma un documento sin
 *     llamar a Auth de forma sincrona.
 */
export function bindAuthConsumers(
  register: (eventType: string, handler: (env: IncomingEnvelope) => Promise<void>) => void,
  deps: {
    userPermissions: UserPermissionsProjectionRepository;
    userDirectory: UserDirectoryRepository;
    rolePermissions: RolePermissionsProjectionRepository;
    customerPortalAccounts: CustomerPortalAccountRepository;
  },
): void {
  register('auth.user.roles_changed.v1', async (env) => {
    const userId = getString(env.payload, 'userId') ?? getString(env.payload, 'UserId');
    if (!userId) return;
    // Nombres de campo de UserRolesChangedIntegrationEvent.cs — Wolverine serializa el
    // envelope en camelCase sobre RabbitMQ (no PascalCase como el nombre del campo C#), asi
    // que hay que aceptar ambas formas. Antes se leia solo 'PermissionCodes'/'PermissionsVersion'/
    // 'RoleIds' (PascalCase), que nunca matcheaban contra el payload real ('permissionCodes'/
    // 'permissionsVersion'/'roleIds') — esta proyeccion quedaba siempre vacia en silencio.
    // Gap real encontrado en produccion post-Fase 7.5.9, no solo el de Fase 1 original.
    const permissions = getStringArray(env.payload, 'permissionCodes', 'PermissionCodes');
    const permVersion = getNumber(env.payload, 'permissionsVersion', 'PermissionsVersion') ?? 1;
    // Fase 2 del plan de notificaciones dinamicas — RoleIds, para poder correlacionar
    // "a este usuario le pega este cambio" cuando llegue RolePermissionsChangedIntegrationEvent.
    const roleIds = getStringArray(env.payload, 'roleIds', 'RoleIds');
    const actorType =
      getString(env.payload, 'actorType') ?? getString(env.payload, 'ActorType') ?? 'TenantEmployee';
    await deps.userPermissions.upsert({
      userId,
      tenantId: env.tenantId,
      permissions,
      permissionVersion: permVersion,
      roleIds,
      actorType,
      isActive: true,
      updatedAtUtc: new Date(),
    });
  });

  register('auth.user.registered.v1', async (env) => {
    const userId = getString(env.payload, 'userId') ?? getString(env.payload, 'UserId');
    if (!userId) return;
    const permissions = getStringArray(env.payload, 'permissions', 'Permissions');
    const actorType =
      getString(env.payload, 'actorType') ?? getString(env.payload, 'ActorType') ?? 'TenantEmployee';
    await deps.userPermissions.upsert({
      userId,
      tenantId: env.tenantId,
      permissions,
      permissionVersion: 1,
      roleIds: [], // un usuario recien registrado todavia no tiene roles asignados
      actorType,
      isActive: true,
      updatedAtUtc: new Date(),
    });

    const email = getString(env.payload, 'email') ?? getString(env.payload, 'Email');
    if (email) {
      // Name/LastName son opcionales en UserRegisteredIntegrationEvent (compat
      // con consumers viejos). Si faltan (evento de una version anterior de Auth
      // durante un rolling deploy), usamos el email como displayName — sigue
      // siendo legible, a diferencia del userId (UUID) que usan los handlers de
      // socket como placeholder cuando no hay entrada en el directorio.
      const name = getString(env.payload, 'name') ?? getString(env.payload, 'Name');
      const lastName = getString(env.payload, 'lastName') ?? getString(env.payload, 'LastName');
      const displayName = name && lastName ? `${name} ${lastName}` : email;
      await deps.userDirectory.upsert({
        userId,
        tenantId: env.tenantId,
        displayName,
        email,
        isActive: true,
        actorType,
      });
    }

    // Fase 6: solo los usuarios de portal traen CustomerId — un TenantEmployee no tiene uno.
    const customerId = getString(env.payload, 'customerId') ?? getString(env.payload, 'CustomerId');
    if (actorType === 'CustomerPortal' && customerId) {
      await deps.customerPortalAccounts.upsert({ customerId, tenantId: env.tenantId, userId });
    }
  });

  register('auth.user.profile_updated.v1', async (env) => {
    const userId = getString(env.payload, 'userId') ?? getString(env.payload, 'UserId');
    const name = getString(env.payload, 'name') ?? getString(env.payload, 'Name');
    const lastName = getString(env.payload, 'lastName') ?? getString(env.payload, 'LastName');
    if (!userId || !name || !lastName) return;

    // No creamos una entrada nueva aqui: sin Email (que este evento no lleva)
    // escribiriamos un directory entry incompleto. Si `registered` aun no proceso
    // (orden de entrega no garantizado entre eventos), el proximo `registered`
    // va a sobreescribir el displayName igual, asi que perder este update puntual
    // es aceptable.
    const existing = await deps.userDirectory.findByUserId(userId);
    if (!existing) return;

    await deps.userDirectory.upsert({
      userId,
      tenantId: env.tenantId,
      displayName: `${name} ${lastName}`,
      email: existing.email,
      isActive: existing.isActive,
    });
  });

  register('auth.user.deactivated.v1', async (env) => {
    const userId = getString(env.payload, 'userId') ?? getString(env.payload, 'UserId');
    if (!userId) return;
    await deps.userPermissions.markInactive(userId, new Date());
    await deps.userDirectory.markInactive(userId);
    await deps.customerPortalAccounts.markInactiveByUserId(userId);
  });

  // Fase 2 del plan de notificaciones dinamicas. Sin este consumer, editar los permisos de
  // un rol con 50 empleados asignados nunca propaga a esta proyeccion — quedan con datos
  // viejos hasta que a cada uno individualmente le vuelvan a tocar su rol (que puede no pasar
  // nunca). PermissionCodes del evento es el set COMPLETO del rol post-cambio, no un diff.
  register('auth.role.permissions_changed.v1', async (env) => {
    const roleId = getString(env.payload, 'roleId') ?? getString(env.payload, 'RoleId');
    const roleName = getString(env.payload, 'roleName') ?? getString(env.payload, 'RoleName');
    if (!roleId || !roleName) return;
    const permissionCodes = getStringArray(env.payload, 'permissionCodes', 'PermissionCodes');
    const permissionsVersion = getNumber(env.payload, 'permissionsVersion', 'PermissionsVersion') ?? 1;

    await deps.rolePermissions.upsert({
      roleId,
      tenantId: env.tenantId,
      roleName,
      permissionCodes,
      permissionsVersion,
    });

    const affectedUsers = await deps.userPermissions.findActiveByTenantAndRoleId(env.tenantId, roleId);
    if (affectedUsers.length === 0) return;

    // Union de permisos por-rol cacheados — un usuario con VARIOS roles no puede
    // sobrescribirse solo con los codigos del rol que cambio, o perderia los permisos
    // heredados de sus otros roles.
    const allRoleIds = [...new Set(affectedUsers.flatMap((user) => user.roleIds))];
    const rolesById = new Map(
      (await deps.rolePermissions.findByRoleIds(allRoleIds)).map((role) => [role.roleId, role]),
    );

    for (const user of affectedUsers) {
      const union = new Set<string>();
      for (const userRoleId of user.roleIds) {
        const role = rolesById.get(userRoleId);
        if (role) role.permissionCodes.forEach((code) => union.add(code));
      }
      await deps.userPermissions.upsert({
        userId: user.userId,
        tenantId: user.tenantId,
        permissions: [...union],
        permissionVersion: user.permissionVersion,
        roleIds: user.roleIds,
        actorType: user.actorType,
        isActive: user.isActive,
        updatedAtUtc: new Date(),
      });
    }
  });
}

function getString(source: Record<string, unknown>, key: string): string | undefined {
  const value = source[key];
  return typeof value === 'string' ? value : undefined;
}

function getNumber(source: Record<string, unknown>, ...keys: string[]): number | undefined {
  for (const key of keys) {
    const value = source[key];
    if (typeof value === 'number') return value;
    if (typeof value === 'string') {
      const parsed = Number.parseInt(value, 10);
      if (Number.isFinite(parsed)) return parsed;
    }
  }
  return undefined;
}

function getStringArray(
  source: Record<string, unknown>,
  ...keys: string[]
): readonly string[] {
  for (const k of keys) {
    const v = source[k];
    if (Array.isArray(v)) return v.filter((x: unknown): x is string => typeof x === 'string');
    if (typeof v === 'string' && v.length > 0) return v.split(' ').filter(Boolean);
  }
  return [];
}
