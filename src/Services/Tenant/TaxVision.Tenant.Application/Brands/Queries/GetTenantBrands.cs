using System.Linq;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using TaxVision.Tenant.Application.Brands.Abstractions;
using TaxVision.Tenant.Domain.Brands;
using TaxVision.Tenant.Domain.Enums;

namespace TaxVision.Tenant.Application.Brands.Queries;

public sealed record GetTenantBrandsQuery(Guid TenantId);

public sealed record TenantBrandsResponse(IReadOnlyList<BrandResponse> Brands);

/// <summary>
/// Devuelve una marca resuelta por CADA superficie configurable (CRM y Portal en v1), tenga o no el
/// tenant filas propias — así la UI del admin puede mostrar/editar ambas, cayendo al default donde
/// no personalizó. No se cachea: es la pantalla de configuración, de baja frecuencia.
/// </summary>
public static class GetTenantBrandsHandler
{
    public static async Task<Result<TenantBrandsResponse>> Handle(
        GetTenantBrandsQuery query,
        ITenantBrandRepository repo,
        CancellationToken ct
    )
    {
        var tenantBrands = await repo.ListAsync(query.TenantId, ct);
        var platformBrands =
            query.TenantId == PlatformTenant.Id ? tenantBrands : await repo.ListAsync(PlatformTenant.Id, ct);

        var resolved = BrandSurfaces
            .Configurable.Select(surface =>
                BrandResolution.Resolve(surface, Find(tenantBrands, surface), Find(platformBrands, surface))
            )
            .ToArray();

        return Result.Success(new TenantBrandsResponse(resolved));
    }

    private static TenantBrand? Find(IReadOnlyList<TenantBrand> brands, BrandSurface surface) =>
        brands.FirstOrDefault(b => b.Surface == surface);
}
