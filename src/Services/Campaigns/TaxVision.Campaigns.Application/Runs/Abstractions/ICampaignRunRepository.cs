using BuildingBlocks.Common;
using TaxVision.Campaigns.Domain.Runs;

namespace TaxVision.Campaigns.Application.Runs.Abstractions;

public interface ICampaignRunRepository
{
    /// <summary>Carga un run con sus unidades (recipients) por <c>(TenantId, Id)</c>.</summary>
    Task<CampaignRun?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default);

    Task<PagedResult<CampaignRun>> ListByCampaignAsync(
        Guid tenantId,
        Guid campaignId,
        int page,
        int size,
        CancellationToken ct = default
    );

    Task AddAsync(CampaignRun run, CancellationToken ct = default);
}
