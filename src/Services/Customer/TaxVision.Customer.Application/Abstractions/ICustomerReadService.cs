using BuildingBlocks.Common;
using TaxVision.Customer.Application.Customers;
using TaxVision.Customer.Application.Customers.Catalogs;

namespace TaxVision.Customer.Application.Abstractions;

public interface ICustomerReadService
{
    /// <summary>Catálogo curado de ocupaciones (global). `search` filtra por nombre; ordenado por DisplayOrder.</summary>
    Task<IReadOnlyList<OccupationResponse>> ListOccupationsAsync(string? search, CancellationToken ct = default);

    /// <summary>Catálogo curado de actividades NAICS (global). `search` filtra por código o descripción.</summary>
    Task<IReadOnlyList<BusinessActivityResponse>> ListBusinessActivitiesAsync(
        string? search,
        CancellationToken ct = default
    );

    /// <param name="assignedToUserId">Si no es null, restringe a los clientes asignados a ese usuario
    /// (visibilidad por asignación). null = sin restricción (admin/view_all o flag apagado).</param>
    /// <param name="includeAssignees">Solo si es true se devuelven los AssigneeUserIds (roster de asignados,
    /// solo para admin/view_all). Un no-admin NO recibe el roster (need-to-know).</param>
    Task<PagedResult<CustomerSummaryResponse>> SearchAsync(
        Guid tenantId,
        string? term,
        CustomerStatusFilter status,
        int page,
        int size,
        Guid? assignedToUserId = null,
        bool includeAssignees = false,
        CancellationToken ct = default
    );

    /// <summary>Resumen para el dashboard: total exacto + altas por mes (últimos <paramref name="months"/>) + últimas altas.</summary>
    Task<CustomerDirectoryOverviewResponse> GetOverviewAsync(
        Guid tenantId,
        int months,
        Guid? assignedToUserId = null,
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
    /// Cross-tenant, paginado: clientes CON asignaciones + su set de staff + versión, para que los
    /// microservicios con proyección de visibilidad por-cliente la SIEMBREN contra la fuente autoritativa.
    /// Solo lo pagina el token de la PlatformTenant (gate en el controller). Los clientes admin-only (sin
    /// fila) no viajan: la ausencia de fila = el empleado no lo ve (el admin lo ve por view_all).
    /// </summary>
    Task<PagedResult<CustomerAssignmentsReconciliationResponse>> ListAssignmentsForReconciliationAsync(
        int page,
        int size,
        CancellationToken ct = default
    );

    /// <summary>
    /// Ficha de detalle del cliente: escalares + direcciones, contactos, relaciones y perfil fiscal
    /// (enmascarado). Proyección de lectura pura — NO carga el agregado del write path.
    /// </summary>
    /// <param name="includeAssignees">Solo si es true se devuelve la lista Assignees (roster, solo admin/view_all).</param>
    Task<CustomerDetailResponse?> GetDetailByIdAsync(
        Guid tenantId,
        Guid customerId,
        Guid? assignedToUserId = null,
        bool includeAssignees = false,
        CancellationToken ct = default
    );

    Task<CustomerExistsResponse> CheckExistsAsync(
        Guid tenantId,
        string? email,
        string? taxIdentifier,
        CancellationToken ct = default
    );
}
