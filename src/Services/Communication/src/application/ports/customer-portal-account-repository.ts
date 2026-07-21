/**
 * Proyeccion local de `(CustomerId -> UserId de portal activo)`. Fase 6 del plan de
 * notificaciones dinamicas — ver docblock del modelo Prisma `CustomerPortalAccount`
 * para el porque (Auth.User.CustomerId no garantiza unicidad ni existencia).
 */
export interface CustomerPortalAccountSnapshot {
  readonly customerId: string;
  readonly tenantId: string;
  readonly userId: string;
  readonly isActive: boolean;
}

export interface CustomerPortalAccountRepository {
  upsert(snapshot: { customerId: string; tenantId: string; userId: string }): Promise<void>;

  /** UserDeactivatedIntegrationEvent solo trae UserId, no CustomerId — de ahi este método en vez de markInactive(customerId). */
  markInactiveByUserId(userId: string): Promise<void>;

  /** null si el customer no tiene cuenta de portal (nunca se creo) o si esta inactiva. */
  findActiveByCustomerId(customerId: string): Promise<CustomerPortalAccountSnapshot | null>;

  /**
   * Fase B4 (chat tipado) — lookup inverso: dado el UserId de un actor
   * 'CustomerPortal' (ya conocido, viene del JWT o de UserDirectoryEntry),
   * resolver a que CustomerId pertenece. Necesario para calcular
   * isPrimaryPreparer al iniciar un chat directo — el evento de Customer solo
   * trae CustomerId, nunca el UserId de portal directamente.
   */
  findActiveByUserId(userId: string): Promise<CustomerPortalAccountSnapshot | null>;
}
