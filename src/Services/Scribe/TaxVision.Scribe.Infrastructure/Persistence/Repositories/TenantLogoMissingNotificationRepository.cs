using Microsoft.EntityFrameworkCore;
using TaxVision.Scribe.Application.Abstractions;
using TaxVision.Scribe.Domain.Projections;

namespace TaxVision.Scribe.Infrastructure.Persistence.Repositories;

public sealed class TenantLogoMissingNotificationRepository(ScribeDbContext dbContext)
    : ITenantLogoMissingNotificationRepository
{
    public Task<TenantLogoMissingNotification?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default) =>
        dbContext.TenantLogoMissingNotifications.FirstOrDefaultAsync(n => n.TenantId == tenantId, ct);

    public async Task AddAsync(TenantLogoMissingNotification notification, CancellationToken ct = default) =>
        await dbContext.TenantLogoMissingNotifications.AddAsync(notification, ct);
}
