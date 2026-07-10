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
    actorType: string;
    isActive: boolean;
    updatedAtUtc: Date;
  }): Promise<void>;

  findByUserId(userId: string): Promise<UserPermissionsProjectionSnapshot | null>;

  markInactive(userId: string, now: Date): Promise<void>;
}
