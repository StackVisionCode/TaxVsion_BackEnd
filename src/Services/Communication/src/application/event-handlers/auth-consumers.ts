import type { UserPermissionsProjectionRepository } from '../ports/user-permissions-projection-repository.js';
import type { UserDirectoryRepository } from '../ports/user-directory-repository.js';
import type { IncomingEnvelope } from '../ports/event-consumer.js';

/**
 * Auth consumers — mantienen al dia dos proyecciones locales:
 *   - `UserPermissionsProjection`: autorizacion fuera del request/socket.
 *   - `UserDirectoryEntry`: displayName real para payloads de chat/call/meeting
 *     (cierra el placeholder userId-como-nombre documentado en chat-handlers.ts).
 */
export function bindAuthConsumers(
  register: (eventType: string, handler: (env: IncomingEnvelope) => Promise<void>) => void,
  deps: { userPermissions: UserPermissionsProjectionRepository; userDirectory: UserDirectoryRepository },
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
      });
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
