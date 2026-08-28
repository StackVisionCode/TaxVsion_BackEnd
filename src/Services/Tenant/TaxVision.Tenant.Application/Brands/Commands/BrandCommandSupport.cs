using BuildingBlocks.Caching;
using TaxVision.Tenant.Application.Brands.Abstractions;
using TaxVision.Tenant.Domain.Brands;
using TaxVision.Tenant.Domain.Enums;

namespace TaxVision.Tenant.Application.Brands.Commands;

/// <summary>Piezas compartidas por los comandos de marca: creación lazy e invalidación de cache.</summary>
internal static class BrandCommandSupport
{
    /// <summary>La marca de una superficie se crea sola la primera vez que se configura (no hay
    /// endpoint "crear marca"). Devuelve la existente o una nueva ya agregada al repo.</summary>
    public static async Task<TenantBrand> GetOrCreateAsync(
        ITenantBrandRepository repo,
        Guid tenantId,
        BrandSurface surface,
        CancellationToken ct
    )
    {
        var brand = await repo.GetAsync(tenantId, surface, ct);
        if (brand is not null)
            return brand;

        brand = TenantBrand.Create(tenantId, surface);
        await repo.AddAsync(brand, ct);
        return brand;
    }

    public static Task InvalidateAsync(
        ICacheService cache,
        Guid tenantId,
        BrandSurface surface,
        CancellationToken ct
    ) => cache.RemoveAsync(BrandCacheKeys.Brand(tenantId, surface), ct);
}
