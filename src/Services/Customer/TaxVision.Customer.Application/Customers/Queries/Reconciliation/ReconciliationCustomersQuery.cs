namespace TaxVision.Customer.Application.Customers.Queries.Reconciliation;

/// <summary>
/// Enumera TODOS los customers de TODOS los tenants (paginado) para que los microservicios con
/// proyecciones locales de customer se auto-reconcilien contra la fuente autoritativa. NO lleva
/// TenantId: es una consulta cross-tenant, autorizada solo para el token de la PlatformTenant en
/// <c>InternalCustomersController.Reconciliation</c>.
/// </summary>
public sealed record ReconciliationCustomersQuery(CustomerStatusFilter Status, int Page, int Size);
