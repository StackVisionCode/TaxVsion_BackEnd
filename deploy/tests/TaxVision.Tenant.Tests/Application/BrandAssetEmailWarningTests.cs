using TaxVision.Tenant.Application.Brands.Commands;
using TaxVision.Tenant.Domain.Enums;

namespace TaxVision.Tenant.Tests.Application;

/// <summary>
/// El aviso del SVG. Un logo SVG se ve perfecto en el CRM y el correo no lo renderiza, así que el
/// dueño lo subía y nunca se enteraba de que sus correos salían sin él.
///
/// <para>Es un aviso, no un rechazo: el SVG es válido para la web y el asset se sube igual. Lo que
/// cambia es que ahora se lo decimos.</para>
/// </summary>
public sealed class BrandAssetEmailWarningTests
{
    [Fact]
    public void An_svg_crm_logo_warns_that_email_will_not_use_it()
    {
        var warning = BrandAssetEmailWarnings.For(BrandSurface.Crm, BrandAssetKey.Logo, "image/svg+xml");

        Assert.NotNull(warning);
        Assert.Contains("SVG", warning);
        Assert.Contains("PNG", warning);
    }

    [Theory]
    [InlineData("image/png")]
    [InlineData("image/jpeg")]
    public void A_raster_crm_logo_says_nothing(string contentType)
    {
        Assert.Null(BrandAssetEmailWarnings.For(BrandSurface.Crm, BrandAssetKey.Logo, contentType));
    }

    [Fact]
    public void The_portal_logo_says_nothing_because_it_never_reaches_email()
    {
        // Solo el logo del CRM alimenta Scribe (TenantBrandAssetScanResultConsumer). Avisar acá sería
        // un aviso falso.
        Assert.Null(BrandAssetEmailWarnings.For(BrandSurface.Portal, BrandAssetKey.Logo, "image/svg+xml"));
    }

    [Fact]
    public void The_favicon_says_nothing_either()
    {
        Assert.Null(BrandAssetEmailWarnings.For(BrandSurface.Crm, BrandAssetKey.Favicon, "image/svg+xml"));
    }

    [Fact]
    public void The_content_type_match_is_case_insensitive()
    {
        Assert.NotNull(BrandAssetEmailWarnings.For(BrandSurface.Crm, BrandAssetKey.Logo, "IMAGE/SVG+XML"));
    }
}
