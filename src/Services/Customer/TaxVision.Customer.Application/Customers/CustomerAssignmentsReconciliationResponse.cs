namespace TaxVision.Customer.Application.Customers;

// DTO para SEMBRAR (reconciliar) las proyecciones de visibilidad por-cliente de otros microservicios.
// Cross-tenant (lo pagina el token de la PlatformTenant vía internal/customers/assignments/reconciliation).
// Solo clientes CON asignaciones (los admin-only no llevan fila → no viajan). Version = UpdatedAtUtc del
// cliente como baseline; los eventos snapshot posteriores traen una Version más nueva y ganan.
public sealed record CustomerAssignmentsReconciliationResponse(
    Guid TenantId,
    Guid CustomerId,
    IReadOnlyList<Guid> AssigneeUserIds,
    Guid? PrimaryUserId,
    DateTime Version
);
