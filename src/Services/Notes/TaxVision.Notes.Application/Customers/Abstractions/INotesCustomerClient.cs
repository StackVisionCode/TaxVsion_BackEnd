using BuildingBlocks.Common;
using TaxVision.Notes.Domain.Projections;

namespace TaxVision.Notes.Application.Customers.Abstractions;

/// <summary>
/// Cliente M2M hacia Customer.Api (<c>GET /customers/internal/list</c>, policy <c>ServiceOnly</c>)
/// usado por el backfill reactivo y el job de reconciliación de nombres (Fase 4B). Desacoplado del
/// contrato HTTP real de Customer — solo expone lo que Notes necesita.
/// </summary>
public interface INotesCustomerClient
{
    /// <summary>Devuelve <c>null</c> si el token de servicio no pudo obtenerse o la llamada HTTP falló — el caller decide cómo reintentar/loguear, nunca lanza.</summary>
    Task<PagedResult<RemoteCustomerSummary>?> ListActiveCustomersAsync(
        Guid tenantId,
        int page,
        int size,
        CancellationToken ct = default
    );

    /// <summary>
    /// Stream global cross-tenant hacia <c>GET customers/internal/reconciliation</c> (todos los
    /// tenants de una vez, token de <c>PlatformTenant</c>) — lo consume
    /// <c>TenantCustomerFullReconciliationJob</c> para el backfill de FILAS FALTANTES que el job de
    /// nombres nunca hizo. Devuelve <c>null</c> en cualquier fallo de token/HTTP, nunca lanza.
    /// </summary>
    Task<RemoteCustomerReconciliationPage?> ListAllForReconciliationAsync(
        int page,
        int size,
        CancellationToken ct = default
    );
}

/// <summary>Proyección mínima de un customer remoto — solo lo que el backfill/reconciliación necesita.</summary>
public sealed record RemoteCustomerSummary(Guid Id, string DisplayName, bool IsActive);

/// <summary>Una página del stream global de reconciliación de customers (cross-tenant).</summary>
public sealed record RemoteCustomerReconciliationPage(IReadOnlyList<RemoteReconciliationCustomer> Items, bool HasMore);

/// <summary>
/// Fila cross-tenant de la fuente autoritativa (Customer). Notes solo necesita identidad + nombre +
/// status para poblar <see cref="CustomerDirectoryEntry"/> (no email, no otro estado).
/// </summary>
public sealed record RemoteReconciliationCustomer(
    Guid TenantId,
    Guid CustomerId,
    string DisplayName,
    CustomerDirectoryStatus Status
);
