/**
 * Proyeccion local de `(CustomerId → PreparerUserId)` alimentada por
 * `customer.preparer_assigned.v1`/`customer.preparer_unassigned.v1` (fuente de
 * verdad: `Customer.AssignedPreparerUserId` en el servicio Customer). Un
 * customer tiene a lo sumo un preparador activo a la vez — mismo invariante
 * que protege el aggregate `Customer`.
 */
export interface CustomerPreparerAssignmentSnapshot {
  readonly customerId: string;
  readonly tenantId: string;
  readonly preparerUserId: string;
  readonly assignedAtUtc: Date;
}

export interface CustomerPreparerAssignmentRepository {
  assign(input: { customerId: string; tenantId: string; preparerUserId: string }): Promise<void>;

  unassign(customerId: string): Promise<void>;

  findByCustomerId(tenantId: string, customerId: string): Promise<CustomerPreparerAssignmentSnapshot | null>;
}
