using BuildingBlocks.Common;
using TaxVision.Campaigns.Domain.Templates;

namespace TaxVision.Campaigns.Application.Templates.Abstractions;

/// <summary>Repositorio del aggregate <see cref="CampaignTemplate"/> (incluye su contenido por canal).</summary>
public interface ICampaignTemplateRepository
{
    Task<CampaignTemplate?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default);
    Task<PagedResult<CampaignTemplate>> ListAsync(Guid tenantId, int page, int size, CancellationToken ct = default);
    Task AddAsync(CampaignTemplate template, CancellationToken ct = default);
    void Remove(CampaignTemplate template);
}
