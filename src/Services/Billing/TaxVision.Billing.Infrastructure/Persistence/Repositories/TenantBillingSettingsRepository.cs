using Microsoft.EntityFrameworkCore;
using TaxVision.Billing.Application.Abstractions;
using TaxVision.Billing.Domain.Settings;

namespace TaxVision.Billing.Infrastructure.Persistence.Repositories;

public sealed class TenantBillingSettingsRepository(BillingDbContext dbContext) : ITenantBillingSettingsRepository
{
    // IgnoreQueryFilters + tenant explícito: también se lee desde CreateInvoiceDraft (bus.InvokeAsync).
    public Task<TenantBillingSettings?> GetByTenantAsync(Guid tenantId, CancellationToken ct = default) =>
        dbContext.TenantBillingSettings.IgnoreQueryFilters().FirstOrDefaultAsync(s => s.TenantId == tenantId, ct);

    public async Task AddAsync(TenantBillingSettings settings, CancellationToken ct = default) =>
        await dbContext.TenantBillingSettings.AddAsync(settings, ct);
}
