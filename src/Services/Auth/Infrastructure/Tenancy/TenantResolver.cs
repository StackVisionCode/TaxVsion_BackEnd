using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.TenantDomains;

namespace TaxVision.Auth.Infrastructure.Tenancy;

public sealed class TenantResolver(
    ITenantDomainRepository domains,
    ITenantRegistry tenants,
    ITenantResolutionCache cache
) : ITenantResolver
{
    public async Task<HostResolutionResult> ResolveAsync(string? host, CancellationToken ct = default)
    {
        var normalizedHost = host?.Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(normalizedHost))
            return HostResolutionResult.Unresolved(TenantResolutionFailureReason.HostMissing);

        if (await cache.TryGetAsync(normalizedHost, ct) is { } cachedTenantId)
            return HostResolutionResult.Resolved(cachedTenantId);

        var domain = await domains.GetByHostAsync(normalizedHost, ct);
        if (domain is null || domain.Status != TenantDomainStatus.Active)
            return HostResolutionResult.Unresolved(TenantResolutionFailureReason.HostUnknown);

        var tenant = await tenants.GetByIdAsync(domain.TenantId, ct);
        if (tenant is null || !tenant.IsActive)
            return HostResolutionResult.Unresolved(TenantResolutionFailureReason.TenantInactive);

        await cache.SetAsync(normalizedHost, domain.TenantId, ct);
        return HostResolutionResult.Resolved(domain.TenantId);
    }
}
