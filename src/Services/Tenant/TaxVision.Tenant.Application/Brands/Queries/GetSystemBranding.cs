using System.Linq;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using TaxVision.Tenant.Application.Brands.Abstractions;
using TaxVision.Tenant.Domain.Enums;

namespace TaxVision.Tenant.Application.Brands.Queries;

/// <summary>
/// Marca del SISTEMA (tenant de plataforma) para el login CENTRAL (app.*/localhost), que no tiene
/// oficina/slug. Reusa el mismo shape público (<see cref="PublicBrandingResponse"/>) y URLs que el
/// endpoint por slug — solo cambia que el tenant es siempre el de plataforma. NO toca el flujo por
/// slug/subdominio, así que la resolución de oficinas en prod queda intacta.
/// </summary>
public sealed record GetSystemBrandingQuery(BrandSurface Surface);

public static class GetSystemBrandingHandler
{
    public static async Task<Result<PublicBrandingResponse>> Handle(
        GetSystemBrandingQuery query,
        ITenantBrandRepository brandRepo,
        CancellationToken ct
    )
    {
        var brand = await brandRepo.GetAsync(PlatformTenant.Id, query.Surface, ct);
        var resolved = BrandResolution.Resolve(query.Surface, brand, brand);

        var primary = resolved.Colors.Single(c => c.Token == BrandColorToken.Primary.ToString()).Value;
        var accent = resolved.Colors.Single(c => c.Token == BrandColorToken.Accent.ToString()).Value;

        return Result.Success(
            new PublicBrandingResponse(
                primary,
                accent,
                LogoUrl: AssetUrl(resolved, BrandAssetKey.Logo),
                FaviconUrl: AssetUrl(resolved, BrandAssetKey.Favicon)
            )
        );
    }

    private static string? AssetUrl(BrandResponse resolved, BrandAssetKey key)
    {
        var asset = resolved.Assets.FirstOrDefault(a =>
            a.Key == key.ToString() && a.Status == BrandAssetStatus.Confirmed.ToString()
        );
        return asset is null ? null : PublicBrandingRoutes.AssetPath(asset.FileId);
    }
}
