using BuildingBlocks.Common;
using Microsoft.EntityFrameworkCore;
using TaxVision.Campaigns.Application.Templates.Abstractions;
using TaxVision.Campaigns.Domain.Templates;

namespace TaxVision.Campaigns.Infrastructure.Persistence.Repositories;

public sealed class CampaignTemplateRepository(CampaignsDbContext db) : ICampaignTemplateRepository
{
    public Task<CampaignTemplate?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default) =>
        db
            .CampaignTemplates.IgnoreQueryFilters()
            .Include(t => t.Contents)
            .FirstOrDefaultAsync(t => t.Id == id && t.TenantId == tenantId, ct);

    public async Task<PagedResult<CampaignTemplate>> ListAsync(
        Guid tenantId,
        int page,
        int size,
        CancellationToken ct = default
    )
    {
        var query = db
            .CampaignTemplates.IgnoreQueryFilters()
            .Where(t => t.TenantId == tenantId)
            .OrderByDescending(t => t.CreatedAtUtc);
        var totalCount = await query.CountAsync(ct);
        var items = await query.Skip((page - 1) * size).Take(size).Include(t => t.Contents).ToListAsync(ct);
        return new PagedResult<CampaignTemplate>(items, page, size, totalCount);
    }

    public async Task AddAsync(CampaignTemplate template, CancellationToken ct = default) =>
        await db.CampaignTemplates.AddAsync(template, ct);

    public void Remove(CampaignTemplate template) => db.CampaignTemplates.Remove(template);
}
