using Microsoft.EntityFrameworkCore;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Domain.Settings;

namespace TaxVision.Subscription.Infrastructure.Persistence.Repositories;

public sealed class SubscriptionTenantSettingsRepository(SubscriptionDbContext db)
    : ISubscriptionTenantSettingsRepository
{
    public Task<SubscriptionTenantSettings?> GetByTenantIdAsync(Guid tenantId, CancellationToken ct = default) =>
        db.TenantSettings.FirstOrDefaultAsync(settings => settings.TenantId == tenantId, ct);

    public async Task AddAsync(SubscriptionTenantSettings settings, CancellationToken ct = default) =>
        await db.TenantSettings.AddAsync(settings, ct);
}
