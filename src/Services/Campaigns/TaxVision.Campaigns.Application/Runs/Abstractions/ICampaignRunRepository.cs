using BuildingBlocks.Common;
using TaxVision.Campaigns.Domain.Runs;

namespace TaxVision.Campaigns.Application.Runs.Abstractions;

/// <summary>Referencia mínima de un run para el barrido.</summary>
public sealed record StaleRunRef(Guid TenantId, Guid Id);

public interface ICampaignRunRepository
{
    /// <summary>Carga un run con sus unidades (recipients) por <c>(TenantId, Id)</c>.</summary>
    Task<CampaignRun?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default);

    /// <summary>Raíz del run y solo la unidad del resultado: aplicar un resultado no depende del tamaño del run.
    /// Sin esa unidad, el run llega sin unidades y el dominio responde que no la encontró.</summary>
    Task<CampaignRun?> GetForResultAsync(Guid tenantId, Guid runId, string dispatchId, CancellationToken ct = default);

    /// <summary>Igual, buscando el run por el <c>DispatchId</c> — DLR de SMS (seam <c>SourceContext</c>).</summary>
    Task<CampaignRun?> GetForDispatchResultAsync(Guid tenantId, string dispatchId, CancellationToken ct = default);

    /// <summary>Igual, por el <c>RecipientId</c> — DLR de Email (seam <c>CampaignId</c> de Postmaster = RecipientId).</summary>
    Task<CampaignRun?> GetForRecipientResultAsync(Guid tenantId, Guid recipientId, CancellationToken ct = default);

    Task<PagedResult<CampaignRun>> ListByCampaignAsync(
        Guid tenantId,
        Guid campaignId,
        int page,
        int size,
        CancellationToken ct = default
    );

    Task AddAsync(CampaignRun run, CancellationToken ct = default);

    /// <summary>Runs en <c>Dispatching</c> creados antes de <paramref name="createdBeforeUtc"/> (todos los tenants), para el barrido.</summary>
    Task<IReadOnlyList<StaleRunRef>> ListStaleDispatchingAsync(
        DateTime createdBeforeUtc,
        int batchSize,
        CancellationToken ct = default
    );

    /// <summary>Runs en <c>Created</c> creados antes de <paramref name="createdBeforeUtc"/> (todos los tenants).</summary>
    Task<IReadOnlyList<StaleRunRef>> ListCreatedBeforeAsync(
        DateTime createdBeforeUtc,
        int batchSize,
        CancellationToken ct = default
    );

    Task<bool> ExistsAsync(Guid tenantId, Guid runId, CancellationToken ct = default);

    /// <summary>Agendado que disparó el run, sin cargar sus unidades. Null si fue manual o no existe.</summary>
    Task<Guid?> GetScheduleIdAsync(Guid tenantId, Guid runId, CancellationToken ct = default);
}
