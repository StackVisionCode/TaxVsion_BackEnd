/**
 * Proyeccion local de `(UserId → DisplayName, Email)` alimentada por los
 * consumers de `auth.user.registered.v1` y `auth.user.profile_updated.v1`.
 * Se usa para resolver el nombre real de un usuario en payloads de socket
 * (chat, calls, meetings) sin depender de un round-trip HTTP a Auth por cada
 * emit. Mismo patron que `UserPermissionsProjectionRepository`.
 */
export interface UserDirectoryEntrySnapshot {
  readonly userId: string;
  readonly tenantId: string;
  readonly displayName: string;
  readonly email: string;
  readonly isActive: boolean;
  readonly updatedAtUtc: Date;
}

/**
 * NOTA cross-tenant (misma excepcion documentada en UserPermissionsProjectionRepository):
 * `findByUserId` consulta por UserId sin TenantId porque el UserId es globalmente
 * unico (Auth lo garantiza) y el caller (handler de socket) ya conoce el tenant
 * del propio JWT — no hace falta filtrar aqui.
 */
export interface UserDirectoryRepository {
  upsert(snapshot: {
    userId: string;
    tenantId: string;
    displayName: string;
    email: string;
    isActive: boolean;
  }): Promise<void>;

  findByUserId(userId: string): Promise<UserDirectoryEntrySnapshot | null>;

  markInactive(userId: string): Promise<void>;
}
