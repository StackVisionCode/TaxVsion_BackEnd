namespace TaxVision.Customer.Application.Customers.Queries.Reconciliation;

/// <summary>
/// Enumera CROSS-TENANT (paginado) los clientes con asignaciones + su set de staff, para que los
/// microservicios con proyección de visibilidad por-cliente se siembren contra la fuente autoritativa.
/// NO lleva TenantId: autorizada solo para el token de la PlatformTenant en
/// <c>InternalCustomersController.ReconcileAssignments</c>.
/// </summary>
public sealed record ReconcileAssignmentsQuery(int Page, int Size);
