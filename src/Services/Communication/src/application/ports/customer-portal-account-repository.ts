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

  /** Contraparte de `markInactiveByUserId` para `auth.user.reactivated.v1` (que tampoco trae CustomerId):
   * reactiva la cuenta de portal para que el cliente vuelva a ser chateable. Sin esto, un portal
   * reactivado en Auth quedaba `IsActive=false` aquí para siempre (portal activo ≠ chateable). */
  markActiveByUserId(userId: string): Promise<void>;

  /** null si el customer no tiene cuenta de portal (nunca se creo) o si esta inactiva. */
  findActiveByCustomerId(customerId: string): Promise<CustomerPortalAccountSnapshot | null>;

  /**
   * Batch del anterior: resuelve las cuentas de portal activas de varios customers
   * en una sola query. Usado por el autocomplete de customers del CRM para saber
   * a que UserId iniciar el chat directo, sin N+1. Solo devuelve los que tienen
   * cuenta activa (los customers sin portal simplemente no aparecen en el resultado).
   */
  findActiveByCustomerIds(customerIds: readonly string[]): Promise<CustomerPortalAccountSnapshot[]>;

  /**
   * Fase B4 (chat tipado) — lookup inverso: dado el UserId de un actor
   * 'CustomerPortal' (ya conocido, viene del JWT o de UserDirectoryEntry),
   * resolver a que CustomerId pertenece. Necesario para calcular
   * isPrimaryPreparer al iniciar un chat directo — el evento de Customer solo
   * trae CustomerId, nunca el UserId de portal directamente.
   */
  findActiveByUserId(userId: string): Promise<CustomerPortalAccountSnapshot | null>;
}
