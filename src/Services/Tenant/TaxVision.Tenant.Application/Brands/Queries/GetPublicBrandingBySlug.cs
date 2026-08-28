using System.Linq;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using TaxVision.Tenant.Application.Brands.Abstractions;
using TaxVision.Tenant.Application.Tenants.Abstractions;
using TaxVision.Tenant.Domain.Enums;

namespace TaxVision.Tenant.Application.Brands.Queries;

public sealed record GetPublicBrandingBySlugQuery(string Slug, BrandSurface Surface);

/// <summary>Marca pública para el login (pre-auth): colores + URLs de logo/favicon ya construidas.</summary>
public sealed record PublicBrandingResponse(string Primary, string Accent, string? LogoUrl, string? FaviconUrl);

/// <summary>
/// Resuelve la marca por slug para la pantalla de login (sin sesión). Anti-enumeración: si el slug
/// no existe, devuelve la marca del SISTEMA (nunca un 404), así un atacante no puede distinguir una
/// oficina existente de una inexistente por este endpoint. Solo emite URLs de assets CONFIRMADOS —
/// un logo del tenant pendiente de escaneo no se muestra en el login.
/// </summary>
public static class GetPublicBrandingBySlugHandler
{
    public static async Task<Result<PublicBrandingResponse>> Handle(
        GetPublicBrandingBySlugQuery query,
        ITenantRepository tenantRepo,
        ITenantBrandRepository brandRepo,
        CancellationToken ct
    )
    {
        // Slug desconocido → tenant de plataforma (marca del sistema). Nunca revela si existe.
        var tenantId = await tenantRepo.GetIdBySubDomainAsync(query.Slug, ct) ?? PlatformTenant.Id;

        var tenantBrand = await brandRepo.GetAsync(tenantId, query.Surface, ct);
        var platformBrand =
            tenantId == PlatformTenant.Id
                ? tenantBrand
                : await brandRepo.GetAsync(PlatformTenant.Id, query.Surface, ct);

        var resolved = BrandResolution.Resolve(query.Surface, tenantBrand, platformBrand);

        var primary = resolved.Colors.Single(c => c.Token == BrandColorToken.Primary.ToString()).Value;
        var accent = resolved.Colors.Single(c => c.Token == BrandColorToken.Accent.ToString()).Value;

        return Result.Success(
            new PublicBrandingResponse(
                primary,
                accent,
                LogoUrl: PublicAssetUrl(resolved, BrandAssetKey.Logo),
                FaviconUrl: PublicAssetUrl(resolved, BrandAssetKey.Favicon)
            )
        );
    }

    /// <summary>URL pública del asset (solo si está confirmado). El front la usa opaca, nunca ve el fileId.</summary>
    private static string? PublicAssetUrl(BrandResponse resolved, BrandAssetKey key)
    {
        var asset = resolved.Assets.FirstOrDefault(a =>
            a.Key == key.ToString() && a.Status == BrandAssetStatus.Confirmed.ToString()
        );
        return asset is null ? null : PublicBrandingRoutes.AssetPath(asset.FileId);
    }
}

/// <summary>Rutas públicas de branding — un solo lugar para la plantilla, así no se desincroniza del controller.</summary>
public static class PublicBrandingRoutes
{
    public static string AssetPath(Guid fileId) => $"/tenants/branding/assets/{fileId}";
}
