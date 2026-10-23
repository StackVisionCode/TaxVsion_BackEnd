using System.Linq;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using TaxVision.Tenant.Application.Brands.Abstractions;
using TaxVision.Tenant.Domain;
using TaxVision.Tenant.Domain.Enums;

namespace TaxVision.Tenant.Tests.Integration;

/// <summary>
/// Color esperado de la marca del sistema leído de la base real (solo lectura): el guardado para el
/// tenant de plataforma o, si no hay, la constante compilada. Así los tests no dependen de cómo esté
/// la marca local (p. ej. tras un rebrand).
/// </summary>
internal static class SystemBrandProbe
{
    public static async Task<string> ExpectedColorAsync(
        TenantApiFactory factory,
        BrandSurface surface,
        BrandColorToken token
    )
    {
        using var scope = factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<ITenantBrandRepository>();
        var brand = await repo.GetAsync(PlatformTenant.Id, surface);

        var stored = brand?.Colors.FirstOrDefault(c => c.Token == token)?.Color.Value;
        return stored
            ?? (
                token == BrandColorToken.Primary
                    ? SystemBrandingDefaults.PrimaryColor.Value
                    : SystemBrandingDefaults.AccentColor.Value
            );
    }
}
