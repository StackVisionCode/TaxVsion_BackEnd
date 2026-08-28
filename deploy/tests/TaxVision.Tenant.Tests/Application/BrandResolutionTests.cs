using System.Linq;
using TaxVision.Tenant.Application.Brands;
using TaxVision.Tenant.Domain.Brands;
using TaxVision.Tenant.Domain.Enums;

namespace TaxVision.Tenant.Tests.Application;

/// <summary>Cascada de defaults: token del tenant → marca del sistema → constante compilada.</summary>
public sealed class BrandResolutionTests
{
    private static TenantBrand PlatformBrand()
    {
        var brand = TenantBrand.Create(Guid.NewGuid(), BrandSurface.Crm);
        brand.SetColor(BrandColorToken.Primary, "#1E466B");
        brand.SetColor(BrandColorToken.Accent, "#67BAF4");
        return brand;
    }

    private static BrandColorDto Color(BrandResponse r, BrandColorToken token) =>
        r.Colors.Single(c => c.Token == token.ToString());

    [Fact]
    public void With_no_brands_colors_fall_to_compiled_defaults_uncustomized()
    {
        var response = BrandResolution.Resolve(BrandSurface.Crm, tenantBrand: null, platformBrand: null);

        Assert.Equal("#1E466B", Color(response, BrandColorToken.Primary).Value);
        Assert.Equal("#67BAF4", Color(response, BrandColorToken.Accent).Value);
        Assert.False(Color(response, BrandColorToken.Primary).IsCustomized);
        Assert.Empty(response.Assets);
    }

    [Fact]
    public void Platform_colors_win_over_compiled_when_tenant_has_none()
    {
        var platform = TenantBrand.Create(Guid.NewGuid(), BrandSurface.Crm);
        platform.SetColor(BrandColorToken.Primary, "#0F5132"); // sistema personalizado (Fase 4)

        var response = BrandResolution.Resolve(BrandSurface.Crm, tenantBrand: null, platformBrand: platform);

        Assert.Equal("#0F5132", Color(response, BrandColorToken.Primary).Value);
        Assert.False(Color(response, BrandColorToken.Primary).IsCustomized);
    }

    [Fact]
    public void Tenant_color_overrides_and_is_marked_customized_per_token()
    {
        var tenant = TenantBrand.Create(Guid.NewGuid(), BrandSurface.Crm);
        tenant.SetColor(BrandColorToken.Primary, "#AA0000"); // solo primary

        var response = BrandResolution.Resolve(BrandSurface.Crm, tenant, PlatformBrand());

        Assert.Equal("#AA0000", Color(response, BrandColorToken.Primary).Value);
        Assert.True(Color(response, BrandColorToken.Primary).IsCustomized);
        // Accent no lo tocó → cae al default del sistema, no customizado.
        Assert.Equal("#67BAF4", Color(response, BrandColorToken.Accent).Value);
        Assert.False(Color(response, BrandColorToken.Accent).IsCustomized);
    }

    [Fact]
    public void Tenant_confirmed_asset_is_effective_and_customized()
    {
        var tenant = TenantBrand.Create(Guid.NewGuid(), BrandSurface.Crm);
        var fileId = Guid.NewGuid();
        tenant.SetAssetPending(BrandAssetKey.Logo, fileId, "image/png", 100, 40, 40);
        tenant.ConfirmAsset(BrandAssetKey.Logo, fileId, "image/png", 100, 40, 40, DateTime.UtcNow);

        var response = BrandResolution.Resolve(BrandSurface.Crm, tenant, PlatformBrand());

        var logo = Assert.Single(response.Assets);
        Assert.Equal("Logo", logo.Key);
        Assert.Equal(fileId, logo.FileId);
        Assert.Equal("Confirmed", logo.Status);
        Assert.True(logo.IsCustomized);
    }

    [Fact]
    public void Tenant_pending_asset_is_shown_so_the_admin_sees_processing()
    {
        var tenant = TenantBrand.Create(Guid.NewGuid(), BrandSurface.Crm);
        tenant.SetAssetPending(BrandAssetKey.Logo, Guid.NewGuid(), "image/png", 100, null, null);

        var response = BrandResolution.Resolve(BrandSurface.Crm, tenant, platformBrand: null);

        var logo = Assert.Single(response.Assets);
        Assert.Equal("Pending", logo.Status);
        Assert.True(logo.IsCustomized);
    }

    [Fact]
    public void Platform_confirmed_asset_is_effective_when_tenant_has_none()
    {
        var platform = PlatformBrand();
        var fileId = Guid.NewGuid();
        platform.SetAssetPending(BrandAssetKey.Logo, fileId, "image/png", 100, null, null);
        platform.ConfirmAsset(BrandAssetKey.Logo, fileId, "image/png", 100, null, null, DateTime.UtcNow);

        var response = BrandResolution.Resolve(BrandSurface.Crm, tenantBrand: null, platformBrand: platform);

        var logo = Assert.Single(response.Assets);
        Assert.Equal(fileId, logo.FileId);
        Assert.False(logo.IsCustomized);
    }

    [Fact]
    public void Platform_pending_asset_is_never_served()
    {
        var platform = PlatformBrand();
        platform.SetAssetPending(BrandAssetKey.Logo, Guid.NewGuid(), "image/png", 100, null, null); // sin confirmar

        var response = BrandResolution.Resolve(BrandSurface.Crm, tenantBrand: null, platformBrand: platform);

        Assert.Empty(response.Assets);
    }
}
