using System.Linq;
using TaxVision.Tenant.Domain.Enums;

namespace TaxVision.Tenant.Application.Brands;

/// <summary>
/// Política de qué superficies puede configurar un tenant en v1. El enum <see cref="BrandSurface"/>
/// modela también Mobile/Email para el futuro, pero hoy solo CRM y Portal tienen frontend y default
/// del sistema sembrado — parsear una superficie fuera de este set es un 400, no un 500.
/// </summary>
public static class BrandSurfaces
{
    public static readonly IReadOnlyList<BrandSurface> Configurable = [BrandSurface.Crm, BrandSurface.Portal];

    public static bool TryParseConfigurable(string? value, out BrandSurface surface)
    {
        surface = default;
        return Enum.TryParse(value, ignoreCase: true, out surface) && Configurable.Contains(surface);
    }
}

/// <summary>Vocabulario cerrado de tokens de color, parseado desde la ruta/DTO (400 si no encaja).</summary>
public static class BrandColorTokens
{
    public static bool TryParse(string? value, out BrandColorToken token) =>
        Enum.TryParse(value, ignoreCase: true, out token) && Enum.IsDefined(token);
}

/// <summary>Vocabulario cerrado de claves de asset (logo/favicon), parseado desde la ruta (400 si no encaja).</summary>
public static class BrandAssetKeys
{
    public static bool TryParse(string? value, out BrandAssetKey key) =>
        Enum.TryParse(value, ignoreCase: true, out key) && Enum.IsDefined(key);
}
