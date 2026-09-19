using BuildingBlocks.Common;
using TaxVision.Campaigns.Domain.Runs;

namespace TaxVision.Campaigns.Application.Runs.Abstractions;

public interface ICampaignRunRepository
{
    /// <summary>Carga un run con sus unidades (recipients) por <c>(TenantId, Id)</c>.</summary>
    Task<CampaignRun?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default);

    /// <summary>Carga el run dueño de una unidad por su <c>DispatchId</c> — para correlacionar DLR/webhooks de entrega de vuelta a la campaña.</summary>
    Task<CampaignRun?> GetByDispatchIdAsync(Guid tenantId, string dispatchId, CancellationToken ct = default);

    /// <summary>Carga el run dueño de una unidad por su <c>RecipientId</c> (Guid) — DLR de Email (seam <c>CampaignId</c> de Postmaster = RecipientId).</summary>
    Task<CampaignRun?> GetByRecipientIdAsync(Guid tenantId, Guid recipientId, CancellationToken ct = default);

    Task<PagedResult<CampaignRun>> ListByCampaignAsync(
        Guid tenantId,
        Guid campaignId,
        int page,
        int size,
        CancellationToken ct = default
    );

    Task AddAsync(CampaignRun run, CancellationToken ct = default);
}
