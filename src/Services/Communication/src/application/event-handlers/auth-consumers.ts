import type { UserPermissionsProjectionRepository } from '../ports/user-permissions-projection-repository.js';
import type { IncomingEnvelope } from '../ports/event-consumer.js';

/**
 * Auth consumers — mantienen la proyeccion local `UserPermissionsProjection`
 * al dia. Se usa para autorizacion fuera del request/socket (integration event
 * handlers que emiten socket a un user; ver §29 patron similar en Signature).
 */
export function bindAuthConsumers(
  register: (eventType: string, handler: (env: IncomingEnvelope) => Promise<void>) => void,
  deps: { userPermissions: UserPermissionsProjectionRepository },
): void {
  register('auth.user.roles_changed.v1', async (env) => {
    const userId = getString(env.payload, 'userId') ?? getString(env.payload, 'UserId');
    if (!userId) return;
    const permissions = getStringArray(env.payload, 'permissions', 'Permissions');
    const permVersion =
      getNumber(env.payload, 'permissionVersion') ?? getNumber(env.payload, 'PermissionVersion') ?? 1;
    const actorType =
      getString(env.payload, 'actorType') ?? getString(env.payload, 'ActorType') ?? 'TenantEmployee';
    await deps.userPermissions.upsert({
      userId,
      tenantId: env.tenantId,
      permissions,
      permissionVersion: permVersion,
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
      actorType,
      isActive: true,
      updatedAtUtc: new Date(),
    });
  });

  register('auth.user.deactivated.v1', async (env) => {
    const userId = getString(env.payload, 'userId') ?? getString(env.payload, 'UserId');
    if (!userId) return;
    await deps.userPermissions.markInactive(userId, new Date());
  });
}

function getString(source: Record<string, unknown>, key: string): string | undefined {
  const value = source[key];
  return typeof value === 'string' ? value : undefined;
}

function getNumber(source: Record<string, unknown>, key: string): number | undefined {
  const value = source[key];
  if (typeof value === 'number') return value;
  if (typeof value === 'string') {
    const parsed = Number.parseInt(value, 10);
    if (Number.isFinite(parsed)) return parsed;
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
