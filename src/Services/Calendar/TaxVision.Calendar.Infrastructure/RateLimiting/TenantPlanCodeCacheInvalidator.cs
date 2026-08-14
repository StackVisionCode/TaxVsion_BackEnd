using BuildingBlocks.Infrastructure.RateLimiting;
using BuildingBlocks.RateLimiting;

namespace TaxVision.Calendar.Infrastructure.RateLimiting;

internal sealed class TenantPlanCodeCacheInvalidator(CachedTenantPlanCodeReader inner) : ITenantPlanCodeCacheInvalidator
{
    public Task InvalidateAsync(Guid tenantId, CancellationToken ct = default) => inner.InvalidateAsync(tenantId, ct);
}
