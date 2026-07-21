/**
 * Proyeccion local de `(RoleId -> PermissionCodes[], PermissionsVersion)` alimentada por
 * `RolePermissionsChangedIntegrationEvent` (Fase 2, plan de notificaciones dinamicas).
 *
 * Existe unicamente como cache de soporte para `RolePermissionsChangedConsumer`: cuando
 * cambian los permisos de UN rol, para recomputar correctamente la union de permisos de un
 * usuario con VARIOS roles hace falta conocer el set de permisos vigente de CADA uno de sus
 * roles, no solo del que acaba de cambiar. Sin esta cache, sobrescribir
 * `UserPermissionsProjection.Permissions` con solo los codigos del rol que cambio perderia
 * los permisos heredados de sus otros roles.
 */
export interface RolePermissionsProjectionSnapshot {
  readonly roleId: string;
  readonly tenantId: string;
  readonly roleName: string;
  readonly permissionCodes: readonly string[];
  readonly permissionsVersion: number;
  readonly updatedAtUtc: Date;
}

export interface RolePermissionsProjectionRepository {
  upsert(snapshot: {
    roleId: string;
    tenantId: string;
    roleName: string;
    permissionCodes: readonly string[];
    permissionsVersion: number;
  }): Promise<void>;

  findByRoleIds(roleIds: readonly string[]): Promise<readonly RolePermissionsProjectionSnapshot[]>;
}
