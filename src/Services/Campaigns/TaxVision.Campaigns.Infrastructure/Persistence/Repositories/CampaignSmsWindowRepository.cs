using Microsoft.EntityFrameworkCore;
using TaxVision.Campaigns.Application.Settings.SmsWindow;
using TaxVision.Campaigns.Domain.Settings;

namespace TaxVision.Campaigns.Infrastructure.Persistence.Repositories;

public sealed class CampaignSmsWindowRepository(CampaignsDbContext db) : ICampaignSmsWindowRepository
{
    public Task<CampaignSmsWindow?> GetAsync(Guid tenantId, CancellationToken ct = default) =>
        db.CampaignSmsWindows.IgnoreQueryFilters().FirstOrDefaultAsync(w => w.TenantId == tenantId, ct);

    public void Add(CampaignSmsWindow window) => db.CampaignSmsWindows.Add(window);

    public void Remove(CampaignSmsWindow window) => db.CampaignSmsWindows.Remove(window);
}
