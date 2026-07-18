using BuildingBlocks.Common;

namespace TaxVision.Correspondence.Application.Abstractions;

/// <summary>
/// Cliente M2M hacia Customer.Api (<c>GET /customers/internal/list</c>, policy
/// <c>ServiceOnly</c>) usado por el backfill de <c>CustomerEmailAddresses</c> al descubrir un
/// tenant nuevo. Deliberadamente desacoplado del contrato HTTP real de Customer — solo expone lo
/// que Correspondence necesita (id + email primario + si está activo).
/// </summary>
public interface ICorrespondenceCustomerClient
{
    /// <summary>
    /// Devuelve <c>null</c> si el token de servicio no pudo obtenerse o la llamada HTTP falló —
    /// el caller decide cómo reintentar/loguear, nunca lanza.
    /// </summary>
    Task<PagedResult<RemoteCustomerSummary>?> ListActiveCustomersAsync(
        Guid tenantId,
        int page,
        int size,
        CancellationToken ct = default
    );
}

/// <summary>Proyección mínima de un customer remoto — solo lo que el backfill necesita.</summary>
public sealed record RemoteCustomerSummary(Guid Id, string PrimaryEmail, bool IsActive);
