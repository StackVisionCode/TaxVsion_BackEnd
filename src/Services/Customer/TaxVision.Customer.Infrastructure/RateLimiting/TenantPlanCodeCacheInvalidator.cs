using BuildingBlocks.Infrastructure.RateLimiting;
using TaxVision.Customer.Application.RateLimiting.Abstractions;

namespace TaxVision.Customer.Infrastructure.RateLimiting;

internal sealed class TenantPlanCodeCacheInvalidator(CachedTenantPlanCodeReader inner) : ITenantPlanCodeCacheInvalidator
{
    public Task InvalidateAsync(Guid tenantId, CancellationToken ct = default) => inner.InvalidateAsync(tenantId, ct);
}
