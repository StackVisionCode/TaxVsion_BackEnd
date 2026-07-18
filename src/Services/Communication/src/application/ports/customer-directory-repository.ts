/**
 * Proyeccion local de `(CustomerId → DisplayName, Email, IsActive)`
 * alimentada por los consumers de `customer.created.v1`, `customer.updated.v1`
 * y `customer.deactivated.v1`. Decision Fase Backend 10: la invitacion de un
 * customer a un meeting es un hot-path (el host busca/autocompleta por
 * nombre/email al armar la invitacion, ver create-meeting-invitations.ts) —
 * ida y vuelta HTTP a Customer por cada tecleo no escala. Mismo patron que
 * `UserDirectoryRepository`, pero fuente e identidad distintas (CustomerId,
 * no UserId — un customer no tiene cuenta de Auth).
 */
export interface CustomerDirectoryEntrySnapshot {
  readonly customerId: string;
  readonly tenantId: string;
  readonly displayName: string;
  readonly email: string;
  readonly isActive: boolean;
  readonly updatedAtUtc: Date;
}

export interface CustomerDirectoryRepository {
  upsert(snapshot: {
    customerId: string;
    tenantId: string;
    displayName: string;
    email: string;
    isActive: boolean;
  }): Promise<void>;

  findByCustomerId(tenantId: string, customerId: string): Promise<CustomerDirectoryEntrySnapshot | null>;

  markInactive(customerId: string): Promise<void>;

  /** Fase Frontend 5 — ver docblock de UserDirectoryRepository.searchByDisplayNameOrEmail, mismo criterio. */
  searchByDisplayNameOrEmail(
    tenantId: string,
    query: string,
    limit: number,
  ): Promise<CustomerDirectoryEntrySnapshot[]>;
}
