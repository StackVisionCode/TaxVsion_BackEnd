using BuildingBlocks.Common;
using TaxVision.Customer.Application.Customers;

namespace TaxVision.Customer.Application.Abstractions;

public interface ICustomerReadService
{
    Task<PagedResult<CustomerSummaryResponse>> SearchAsync(
        Guid tenantId,
        string? term,
        CustomerStatusFilter status,
        int page,
        int size,
        CancellationToken ct = default
    );

    /// <summary>
    /// Enumera customers de TODOS los tenants (paginado) para la reconciliación M2M de proyecciones.
    /// Cross-tenant a propósito: no lleva tenantId y el llamador debe ser el token de la PlatformTenant
    /// (gate en <c>InternalCustomersController.Reconciliation</c>).
    /// </summary>
    Task<PagedResult<CustomerReconciliationResponse>> ListForReconciliationAsync(
        CustomerStatusFilter status,
        int page,
        int size,
        CancellationToken ct = default
    );

    /// <summary>
    /// Ficha de detalle del cliente: escalares + direcciones, contactos, relaciones y perfil fiscal
    /// (enmascarado). Proyección de lectura pura — NO carga el agregado del write path.
    /// </summary>
    Task<CustomerDetailResponse?> GetDetailByIdAsync(Guid tenantId, Guid customerId, CancellationToken ct = default);

    Task<CustomerExistsResponse> CheckExistsAsync(
        Guid tenantId,
        string? email,
        string? taxIdentifier,
        CancellationToken ct = default
    );
}
