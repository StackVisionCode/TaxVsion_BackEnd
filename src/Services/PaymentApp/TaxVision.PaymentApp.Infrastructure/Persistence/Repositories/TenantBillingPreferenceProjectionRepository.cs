using Microsoft.EntityFrameworkCore;
using TaxVision.PaymentApp.Application.Billing.Abstractions;
using TaxVision.PaymentApp.Domain.Billing;

namespace TaxVision.PaymentApp.Infrastructure.Persistence.Repositories;

/// <summary>Consumer Wolverine sin TenantContext ambiente — ignora el filtro fail-closed. El
/// tenantId viene confiable desde el evento.</summary>
public sealed class TenantBillingPreferenceProjectionRepository(PaymentAppDbContext db)
    : ITenantBillingPreferenceProjectionRepository
{
    public Task<TenantBillingPreferenceProjection?> GetAsync(Guid tenantId, CancellationToken ct = default) =>
        db.TenantBillingPreferenceProjections.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.TenantId == tenantId, ct);

    public async Task AddAsync(TenantBillingPreferenceProjection projection, CancellationToken ct = default) =>
        await db.TenantBillingPreferenceProjections.AddAsync(projection, ct);
}
