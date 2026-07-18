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
  /**
   * Fase Backend 10 — 'TenantEmployee' | 'PlatformSupport' | etc (mismo
   * catalogo que UserPermissionsProjectionRepository.actorType). Permite
   * filtrar "solo empleados" en un lookup sin necesitar una proyeccion
   * EmployeeDirectory separada (decision explicita: NO crearla).
   */
  readonly actorType: string;
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
    actorType?: string;
  }): Promise<void>;

  findByUserId(userId: string): Promise<UserDirectoryEntrySnapshot | null>;

  markInactive(userId: string): Promise<void>;

  /**
   * Fase Frontend 5 — autocomplete de employees al armar invitaciones de
   * meeting. Filtra por TenantId + IsActive; `query` matchea contra
   * DisplayName o Email (contains). Nunca cruza tenants.
   */
  searchByDisplayNameOrEmail(
    tenantId: string,
    query: string,
    limit: number,
  ): Promise<UserDirectoryEntrySnapshot[]>;
}
