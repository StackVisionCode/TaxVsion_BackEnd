using System.Linq;
using TaxVision.Tenant.Domain;
using TaxVision.Tenant.Domain.Brands;
using TaxVision.Tenant.Domain.Enums;
using TaxVision.Tenant.Domain.ValueObjects;

namespace TaxVision.Tenant.Application.Brands;

/// <summary>Un color efectivo + si el tenant lo personalizó (para que la UI muestre "custom/default").</summary>
public sealed record BrandColorDto(string Token, string Value, bool IsCustomized);

/// <summary>
/// Un asset efectivo. <see cref="FileId"/> es la identidad en CloudStorage; el front construye la URL
/// con el endpoint público <c>/tenants/branding/assets/{token}</c> (Fase 5), nunca recibe una URL
/// presignada aquí. <see cref="IsCustomized"/> = el tenant subió el suyo (aunque esté Pending).
/// </summary>
public sealed record BrandAssetDto(
    string Key,
    Guid FileId,
    string Status,
    string ContentType,
    int? Width,
    int? Height,
    bool IsCustomized
);

/// <summary>Marca efectiva de una superficie: colores + assets ya resueltos con la cascada de defaults.</summary>
public sealed record BrandResponse(
    string Surface,
    IReadOnlyList<BrandColorDto> Colors,
    IReadOnlyList<BrandAssetDto> Assets
);

/// <summary>
/// Resuelve la cascada de defaults (Application, no dominio): token del tenant → marca del sistema
/// (tenant de plataforma, misma superficie) → constante compilada (<see cref="SystemBrandingDefaults"/>).
/// Los assets solo cuentan como "efectivos" si están Confirmed; un asset del tenant en Pending se
/// muestra igual (para que el admin vea su "processing"), pero uno de plataforma solo si está listo.
/// </summary>
public static class BrandResolution
{
    private static readonly BrandColorToken[] Tokens = [BrandColorToken.Primary, BrandColorToken.Accent];
    private static readonly BrandAssetKey[] AssetKeys = [BrandAssetKey.Logo, BrandAssetKey.Favicon];

    public static BrandResponse Resolve(BrandSurface surface, TenantBrand? tenantBrand, TenantBrand? platformBrand)
    {
        var colors = Tokens.Select(token => ResolveColor(token, tenantBrand, platformBrand)).ToArray();

        var assets = AssetKeys
            .Select(key => ResolveAsset(key, tenantBrand, platformBrand))
            .Where(asset => asset is not null)
            .Select(asset => asset!)
            .ToArray();

        return new BrandResponse(surface.ToString(), colors, assets);
    }

    private static BrandColorDto ResolveColor(
        BrandColorToken token,
        TenantBrand? tenantBrand,
        TenantBrand? platformBrand
    )
    {
        var tenantColor = FindColor(tenantBrand, token);
        if (tenantColor is not null)
            return new BrandColorDto(token.ToString(), tenantColor.Color.Value, IsCustomized: true);

        var platformColor = FindColor(platformBrand, token);
        var value = platformColor?.Color.Value ?? CompiledDefault(token).Value;
        return new BrandColorDto(token.ToString(), value, IsCustomized: false);
    }

    private static BrandAssetDto? ResolveAsset(BrandAssetKey key, TenantBrand? tenantBrand, TenantBrand? platformBrand)
    {
        // El asset propio del tenant manda aunque esté Pending: el admin necesita ver su "processing".
        var tenantAsset = FindAsset(tenantBrand, key);
        if (tenantAsset is not null)
            return ToDto(key, tenantAsset, isCustomized: true);

        // El default del sistema solo si ya está confirmado (nunca servir un pendiente de plataforma).
        var platformAsset = FindAsset(platformBrand, key);
        if (platformAsset is { Status: BrandAssetStatus.Confirmed })
            return ToDto(key, platformAsset, isCustomized: false);

        return null;
    }

    private static BrandAssetDto ToDto(BrandAssetKey key, TenantBrandAsset asset, bool isCustomized) =>
        new(
            key.ToString(),
            asset.FileId,
            asset.Status.ToString(),
            asset.ContentType,
            asset.Width,
            asset.Height,
            isCustomized
        );

    private static TenantBrandColor? FindColor(TenantBrand? brand, BrandColorToken token) =>
        brand?.Colors.FirstOrDefault(c => c.Token == token);

    private static TenantBrandAsset? FindAsset(TenantBrand? brand, BrandAssetKey key) =>
        brand?.Assets.FirstOrDefault(a => a.Key == key);

    private static HexColor CompiledDefault(BrandColorToken token) =>
        token == BrandColorToken.Primary ? SystemBrandingDefaults.PrimaryColor : SystemBrandingDefaults.AccentColor;
}
