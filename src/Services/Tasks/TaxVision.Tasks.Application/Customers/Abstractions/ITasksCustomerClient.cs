using BuildingBlocks.Common;
using TaxVision.Tasks.Domain.Projections;

namespace TaxVision.Tasks.Application.Customers.Abstractions;

/// <summary>
/// Cliente M2M de solo lectura hacia Customer, usado por el backfill reactivo y por los dos jobs de
/// reconciliación. Expone únicamente lo que Task necesita, no el contrato HTTP completo.
/// </summary>
public interface ITasksCustomerClient
{
    /// <summary>Devuelve <c>null</c> si no se pudo obtener el token o la llamada HTTP falló; nunca lanza.</summary>
    Task<PagedResult<RemoteCustomerSummary>?> ListActiveCustomersAsync(
        Guid tenantId,
        int page,
        int size,
        CancellationToken ct = default
    );

    /// <summary>
    /// Stream cross-tenant: todos los tenants de una vez con token de <c>PlatformTenant</c>. Lo
    /// consume el job que rellena filas faltantes, no sólo nombres. Devuelve <c>null</c> ante
    /// cualquier fallo; nunca lanza.
    /// </summary>
    Task<RemoteCustomerReconciliationPage?> ListAllForReconciliationAsync(
        int page,
        int size,
        CancellationToken ct = default
    );
}

public sealed record RemoteCustomerSummary(Guid Id, string DisplayName, bool IsActive);

public sealed record RemoteCustomerReconciliationPage(IReadOnlyList<RemoteReconciliationCustomer> Items, bool HasMore);

public sealed record RemoteReconciliationCustomer(
    Guid TenantId,
    Guid CustomerId,
    string DisplayName,
    CustomerDirectoryStatus Status
);
