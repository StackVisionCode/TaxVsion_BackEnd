import type {
  UserPermissionsProjectionRepository,
  UserPermissionsProjectionSnapshot,
} from '../../src/application/ports/user-permissions-projection-repository.js';

/**
 * RBAC Fase 7.5.9 — `checkPermission()` (domain/shared/permissions.ts) ya no lee el array
 * `permissions` embebido en el JWT, sino esta proyeccion. Fixture reutilizable para no repetir
 * el mismo objeto default en cada test de permisos.
 */
export function fakeSnapshot(
  overrides: Partial<UserPermissionsProjectionSnapshot> & { userId: string },
): UserPermissionsProjectionSnapshot {
  return {
    tenantId: 'tenant-fake',
    permissions: [],
    permissionVersion: 1,
    roleIds: [],
    actorType: 'TenantEmployee',
    isActive: true,
    updatedAtUtc: new Date(0),
    ...overrides,
  };
}

export function createFakeProjectionRepository(
  snapshots: readonly UserPermissionsProjectionSnapshot[],
): UserPermissionsProjectionRepository {
  const byUserId = new Map(snapshots.map((s) => [s.userId, s]));
  return {
    async upsert(snapshot) {
      byUserId.set(snapshot.userId, { ...snapshot, updatedAtUtc: snapshot.updatedAtUtc });
    },
    async findByUserId(userId) {
      return byUserId.get(userId) ?? null;
    },
    async markInactive(userId, now) {
      const existing = byUserId.get(userId);
      if (existing) byUserId.set(userId, { ...existing, isActive: false, updatedAtUtc: now });
    },
    async findActiveByTenantAndRoleId(tenantId, roleId) {
      return [...byUserId.values()].filter(
        (s) => s.tenantId === tenantId && s.isActive && s.roleIds.includes(roleId),
      );
    },
  };
}
