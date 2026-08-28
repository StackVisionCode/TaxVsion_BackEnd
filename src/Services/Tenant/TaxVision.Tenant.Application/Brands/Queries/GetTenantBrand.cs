using BuildingBlocks.Caching;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using TaxVision.Tenant.Application.Brands.Abstractions;
using TaxVision.Tenant.Domain.Enums;

namespace TaxVision.Tenant.Application.Brands.Queries;

public sealed record GetTenantBrandQuery(Guid TenantId, BrandSurface Surface);

public static class GetTenantBrandHandler
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(5);

    public static async Task<Result<BrandResponse>> Handle(
        GetTenantBrandQuery query,
        ITenantBrandRepository repo,
        ICacheService cache,
        CancellationToken ct
    )
    {
        var cacheKey = BrandCacheKeys.Brand(query.TenantId, query.Surface);
        var response = await cache.GetOrCreateAsync(
            cacheKey,
            innerCt => LoadAsync(query.TenantId, query.Surface, repo, innerCt),
            CacheTtl,
            ct
        );

        // Nunca es null: la cascada siempre resuelve (mínimo la constante compilada).
        return Result.Success(response!);
    }

    private static async Task<BrandResponse> LoadAsync(
        Guid tenantId,
        BrandSurface surface,
        ITenantBrandRepository repo,
        CancellationToken ct
    )
    {
        var tenantBrand = await repo.GetAsync(tenantId, surface, ct);
        // El default del sistema vive en las filas del tenant de plataforma para la misma superficie.
        var platformBrand =
            tenantId == PlatformTenant.Id ? tenantBrand : await repo.GetAsync(PlatformTenant.Id, surface, ct);

        return BrandResolution.Resolve(surface, tenantBrand, platformBrand);
    }
}
