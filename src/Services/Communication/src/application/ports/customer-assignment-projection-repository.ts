/**
 * Proyeccion local del set M:N `(CustomerId → {UserId})` de staff asignado a un
 * customer (P2), alimentada por el snapshot `customer.assignments_changed.v1`
 * (fuente de verdad: las asignaciones del aggregate `Customer`). A diferencia de
 * `CustomerPreparerAssignmentRepository` (1:1, solo el primary, para
 * `isPrimaryPreparer`), esta cubre TODOS los asignados y es la fuente del gate
 * `restrictCustomerChatToAssignedPreparer`: con el modelo M:N, CUALQUIER staff
 * asignado puede chatear con el customer (el primary tambien esta en el set).
 *
 * `Version` (UpdatedAtUtc del customer) da idempotencia: el set se REEMPLAZA solo
 * con snapshots mas nuevos, descartando eventos reordenados o duplicados.
 */
export interface CustomerAssignmentProjectionRepository {
  /** Ultima Version aplicada para el customer, o null si no hay filas. */
  getVersion(tenantId: string, customerId: string): Promise<Date | null>;

  /** Reemplaza el set de asignados del customer (borra + inserta) con la Version dada. */
  replace(tenantId: string, customerId: string, userIds: readonly string[], version: Date): Promise<void>;

  /** ¿Este usuario esta asignado a este customer? (gate de chat). */
  isAssigned(tenantId: string, customerId: string, userId: string): Promise<boolean>;

  /** Set de customerIds asignados a este usuario (para filtrar el picker de clientes por asignacion). */
  getAssignedCustomerIds(tenantId: string, userId: string): Promise<string[]>;
}
