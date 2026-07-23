using Microsoft.EntityFrameworkCore;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Domain.Emailing.Campaigns;

namespace TaxVision.Notification.Infrastructure.Persistence.Repositories;

public sealed class EmailCampaignRepository(NotificationDbContext db) : IEmailCampaignRepository
{
    public async Task AddAsync(EmailCampaign campaign, CancellationToken ct = default) =>
        await db.EmailCampaigns.AddAsync(campaign, ct);

    public async Task<EmailCampaign?> GetByIdAsync(Guid id, Guid tenantId, CancellationToken ct = default) =>
        await db
            .EmailCampaigns.IgnoreQueryFilters()
            .Include(c => c.Recipients)
            .FirstOrDefaultAsync(c => c.Id == id && c.TenantId == tenantId, ct);

    public async Task<EmailCampaign?> GetForProcessingAsync(Guid id, CancellationToken ct = default) =>
        await db.EmailCampaigns.Include(c => c.Recipients).FirstOrDefaultAsync(c => c.Id == id, ct);

    public async Task<EmailCampaign?> GetByIdNoRecipientsAsync(Guid id, CancellationToken ct = default) =>
        await db.EmailCampaigns.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);

    public async Task<IReadOnlyList<EmailCampaignRecipient>> GetRecipientsPageAsync(
        Guid campaignId,
        int skip,
        int take,
        CancellationToken ct = default
    ) =>
        await db
            .EmailCampaignRecipients.AsNoTracking()
            .Where(r => r.CampaignId == campaignId)
            .OrderBy(r => r.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);

    // RBAC Fase 5 — cross-tenant por diseño (CampaignSchedulerService recorre TODOS los tenants
    // en cada corrida), único consumidor de este método. IgnoreQueryFilters() explícito.
    public async Task<IReadOnlyList<EmailCampaign>> GetDueAsync(
        DateTime nowUtc,
        int max,
        CancellationToken ct = default
    ) =>
        await db
            .EmailCampaigns.IgnoreQueryFilters()
            .Where(c => c.Status == CampaignStatus.Scheduled && c.ScheduledAtUtc <= nowUtc)
            .OrderBy(c => c.ScheduledAtUtc)
            .Take(max)
            .ToListAsync(ct);

    public async Task<(IReadOnlyList<EmailCampaign> Items, int TotalCount)> GetPagedAsync(
        Guid tenantId,
        CampaignStatus? status,
        int page,
        int size,
        CancellationToken ct = default
    )
    {
        var query = db.EmailCampaigns.AsNoTracking().IgnoreQueryFilters().Where(c => c.TenantId == tenantId);
        if (status is not null)
            query = query.Where(c => c.Status == status);

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(c => c.CreatedAtUtc)
            .Skip((page - 1) * size)
            .Take(size)
            .ToListAsync(ct);

        return (items, total);
    }
}
