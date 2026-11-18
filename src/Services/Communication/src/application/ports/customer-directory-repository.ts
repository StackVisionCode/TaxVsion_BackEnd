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
  /** null = cliente sin email (D-3). */
  readonly email: string | null;
  readonly isActive: boolean;
  /** Foto vigente de la ficha del cliente; null = sin foto (o archivado). */
  readonly avatarFileId: string | null;
  readonly updatedAtUtc: Date;
}

export interface CustomerDirectoryRepository {
  /**
   * Con `occurredAtUtc` (fecha del aviso) un aviso más viejo que el último aplicado no pisa el perfil, y su
   * estado respeta el orden de los avisos de estado. Sin fecha (reconciliación) se aplica siempre.
   */
  upsert(snapshot: {
    customerId: string;
    tenantId: string;
    displayName: string;
    /** null = el aviso no trae email: nunca borra el conocido (el email no se quita). */
    email: string | null;
    /** undefined = no tocar el estado guardado (la fila nueva nace activa). */
    isActive?: boolean;
    /** undefined = no tocar la foto guardada; null = quitarla. */
    avatarFileId?: string | null;
    occurredAtUtc?: Date;
  }): Promise<void>;

  findByCustomerId(tenantId: string, customerId: string): Promise<CustomerDirectoryEntrySnapshot | null>;

  /**
   * MNT-01: clientes activos de ESA oficina con ese email. El mismo cliente en otra oficina es otra fila
   * (otro customerId) y nunca aparece; puede haber varios en la misma oficina (p. ej. una pareja).
   */
  findActiveByEmail?(tenantId: string, email: string): Promise<CustomerDirectoryEntrySnapshot[]>;

  /** `statusChangedAtUtc` (occurredOn del aviso): si viene, solo aplica si es mas nuevo que el ultimo aplicado. */
  markInactive(customerId: string, statusChangedAtUtc?: Date): Promise<void>;

  /** Cliente restaurado o activado: vuelve al directorio. Mismo control de orden que `markInactive`. */
  markActive(customerId: string, statusChangedAtUtc?: Date): Promise<void>;

  /** Archivar borra la foto en Customer: la proyeccion la limpia igual. */
  clearAvatar(customerId: string): Promise<void>;

  /** Fase Frontend 5 — ver docblock de UserDirectoryRepository.searchByDisplayNameOrEmail, mismo criterio. */
  searchByDisplayNameOrEmail(
    tenantId: string,
    query: string,
    limit: number,
  ): Promise<CustomerDirectoryEntrySnapshot[]>;
}
