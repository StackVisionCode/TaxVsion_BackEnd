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
  /** Escribe el snapshot completo. Solo para eventos que SI transportan permisos
   * (`UserRolesChanged`, `RolePermissionsChanged`); ver `upsertIdentityPreservingPermissions`. */
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

  /**
   * Alta de la fila SIN tocar `permissions`/`permissionVersion`/`roleIds` si ya existe.
   *
   * Para eventos que identifican al usuario pero no transportan permisos —
   * `UserRegisteredIntegrationEvent` es el caso real. Usar `upsert` desde ahí escribía
   * `permissions: []` también en el UPDATE, así que si `registered` llegaba DESPUES de
   * `roles_changed` (el consumer procesa con prefetch > 1, sin orden garantizado) borraba los
   * permisos buenos y dejaba al usuario fail-closed en Communication, sin excepción ni
   * dead-letter. Un evento que no transporta permisos no tiene autoridad para borrarlos.
   */
  upsertIdentityPreservingPermissions(identity: {
    userId: string;
    tenantId: string;
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
