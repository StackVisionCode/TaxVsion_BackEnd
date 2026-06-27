using BuildingBlocks.Caching;
using TaxVision.Tenant.Application.Tenants.Commands;

namespace TaxVision.Tenant.Application.Tenants;

public static class TenantListCache
{
    private const string VersionKey = "tenants:list:version";

    public static async Task<IReadOnlyList<TenantResponse>> GetPageAsync(
        ICacheService cache,
        int page,
        int size,
        Func<CancellationToken, Task<IReadOnlyList<TenantResponse>>> factory,
        CancellationToken ct)
    {
        var version = await cache.GetOrCreateAsync(
            VersionKey,
            _ => Task.FromResult(Guid.NewGuid().ToString("N")),
            TimeSpan.FromHours(24),
            ct);

        return await cache.GetOrCreateAsync(
            $"tenants:list:{version}:page:{page}:size:{size}",
            factory,
            TimeSpan.FromMinutes(5),
            ct);
    }

    public static Task InvalidateAsync(ICacheService cache, CancellationToken ct) =>
        cache.SetAsync(
            VersionKey,
            Guid.NewGuid().ToString("N"),
            TimeSpan.FromHours(24),
            ct);
}
