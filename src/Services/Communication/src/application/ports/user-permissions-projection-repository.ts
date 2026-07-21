/**
 * Proyeccion local de `(UserId, TenantId, Permissions[], PermissionVersion)`
 * alimentada por el consumer de `UserRolesChanged`. Se usa para:
 *   - Invalidar cache de JWKS/permissions cuando el rol cambia.
 *   - Autorizar acciones fuera de banda (integration event handler que emite
 *     socket a un user especifico).
 */
export interface UserPermissionsProjectionSnapshot {
  readonly userId: string;
  readonly tenantId: string;
  readonly permissions: readonly string[];
  readonly permissionVersion: number;
  // Fase 2 del plan de notificaciones dinamicas — RoleIds del usuario, para poder recomputar
  // su union de permisos cuando cambia UN rol (ver RolePermissionsChangedConsumer).
  readonly roleIds: readonly string[];
  readonly actorType: string;
  readonly isActive: boolean;
  readonly updatedAtUtc: Date;
}

/**
 * NOTA cross-tenant (excepcion al filtro global): `findByUserId` y `markInactive`
 * consultan por UserId sin TenantId porque el UserId es globalmente unico (Auth
 * lo garantiza) y este read-model se usa para autorizar acciones fuera-de-banda
 * en integration event handlers donde el tenant aun no esta cargado. Se
 * documenta aqui para que no lo confunda con una violacion del tenant-filter.
 */
export interface UserPermissionsProjectionRepository {
  upsert(snapshot: {
    userId: string;
    tenantId: string;
    permissions: readonly string[];
    permissionVersion: number;
    roleIds: readonly string[];
    actorType: string;
    isActive: boolean;
    updatedAtUtc: Date;
  }): Promise<void>;

  findByUserId(userId: string): Promise<UserPermissionsProjectionSnapshot | null>;

  markInactive(userId: string, now: Date): Promise<void>;

  /** Fase 2 — usuarios activos de un tenant que tienen el RoleId dado entre sus RoleIds.
   * Usado por RolePermissionsChangedConsumer para saber a quien recomputarle Permissions. */
  findActiveByTenantAndRoleId(
    tenantId: string,
    roleId: string,
  ): Promise<readonly UserPermissionsProjectionSnapshot[]>;
}
