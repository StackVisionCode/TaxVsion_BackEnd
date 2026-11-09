using Microsoft.EntityFrameworkCore;
using TaxVision.Notification.Application.Email.Sending.Campaign;
using TaxVision.Notification.Domain.Emailing.Campaigns;

namespace TaxVision.Notification.Infrastructure.Persistence.Repositories;

public sealed class CampaignDispatchSendRepository(NotificationDbContext db) : ICampaignDispatchSendRepository
{
    public Task<CampaignDispatchSend?> FindAsync(Guid tenantId, string dispatchId, CancellationToken ct = default) =>
        db
            .CampaignDispatchSends.IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.DispatchId == dispatchId, ct);

    public async Task AddAsync(CampaignDispatchSend send, CancellationToken ct = default) =>
        await db.CampaignDispatchSends.AddAsync(send, ct);
}
