using BuildingBlocks.Platform;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.Platform;

/// <summary>
/// La regla de "qué host es de una oficina" que comparten Auth y el Gateway. Antes cada uno tenía
/// su copia y discrepaban (el Gateway trataba client.* como oficina; Auth no conocía admin.*).
/// </summary>
public sealed class PlatformHostRulesTests
{
    private static readonly PlatformSettingsSnapshot Platform = new(
        1,
        "TaxOffix",
        "TaxOffix LLC",
        "taxoffix.io",
        "api.taxoffix.io",
        "support@taxoffix.io",
        "billing@taxoffix.io"
    );

    [Theory]
    [InlineData("manfer.taxoffix.io", "manfer")]
    [InlineData("MANFER.TaxOffix.io", "manfer")]
    [InlineData("manfer.taxoffix.io:443", "manfer")]
    [InlineData("manfer.taxoffix.io.", "manfer")]
    public void An_office_host_yields_its_slug(string host, string expectedSlug)
    {
        Assert.True(Platform.TryGetTenantSlug(host, out var slug));
        Assert.Equal(expectedSlug, slug);
    }

    [Theory]
    [InlineData("taxoffix.io")]
    [InlineData("api.taxoffix.io")]
    [InlineData("app.taxoffix.io")]
    [InlineData("client.taxoffix.io")]
    [InlineData("www.taxoffix.io")]
    [InlineData("admin.taxoffix.io")]
    [InlineData("a.b.taxoffix.io")]
    [InlineData("manfer.taxproffice.com")]
    [InlineData("eviltaxoffix.io")]
    [InlineData("localhost")]
    [InlineData("")]
    [InlineData(null)]
    public void Shared_nested_or_foreign_hosts_are_not_offices(string? host)
    {
        Assert.False(Platform.TryGetTenantSlug(host, out _));
    }

    [Theory]
    [InlineData("taxoffix.io")]
    [InlineData("api.taxoffix.io")]
    [InlineData("app.taxoffix.io")]
    [InlineData("client.taxoffix.io")]
    [InlineData("WWW.taxoffix.io")]
    public void The_apex_and_reserved_subdomains_are_shared_hosts(string host)
    {
        Assert.True(Platform.IsSharedHost(host));
    }

    [Theory]
    [InlineData("manfer.taxoffix.io")]
    [InlineData("localhost")]
    [InlineData("archivos.suoficina.com")]
    public void Offices_and_foreign_hosts_are_not_shared(string host)
    {
        Assert.False(Platform.IsSharedHost(host));
    }

    [Fact]
    public void The_rules_follow_the_current_domain()
    {
        var moved = Platform with { BaseDomain = "taxoffix.com", ApiHost = "api.taxoffix.com" };

        Assert.True(moved.TryGetTenantSlug("manfer.taxoffix.com", out _));
        Assert.False(moved.TryGetTenantSlug("manfer.taxoffix.io", out _));
        Assert.Equal("manfer.taxoffix.com", moved.TenantHost("manfer"));
    }

    /// <summary>
    /// El predicado que usa el CORS del Gateway: HTTPS + host de la plataforma (oficina o compartido).
    /// Sin esto, un subdominio de oficina nuevo (<c>manfer.taxoffix.io</c>) hacía fetch a api.* y el
    /// navegador lo bloqueaba porque la lista fija <see cref="PlatformSettingsSnapshot.WebOrigins"/>
    /// solo contempla apex/www/app/client, no los tenants.
    /// </summary>
    [Theory]
    [InlineData("https://taxoffix.io")]
    [InlineData("https://www.taxoffix.io")]
    [InlineData("https://app.taxoffix.io")]
    [InlineData("https://client.taxoffix.io")]
    [InlineData("https://admin.taxoffix.io")]
    [InlineData("https://api.taxoffix.io")]
    [InlineData("https://manfer.taxoffix.io")]
    [InlineData("https://MANFER.TaxOffix.io")]
    public void A_platform_https_origin_is_accepted(string origin)
    {
        Assert.True(Platform.IsPlatformHttpsOrigin(origin));
    }

    [Theory]
    [InlineData("http://manfer.taxoffix.io")] // HTTP plano no cuenta: la plataforma vive sobre TLS.
    [InlineData("http://taxoffix.io")]
    [InlineData("https://a.b.taxoffix.io")] // Dos labels: no es ni oficina ni shared.
    [InlineData("https://eviltaxoffix.io")] // Dominio ajeno que contiene el nombre.
    [InlineData("https://manfer.taxproffice.com")] // Dominio viejo tras el cambio de marca.
    [InlineData("https://localhost")]
    [InlineData("capacitor://localhost")]
    [InlineData("not-a-url")]
    [InlineData("")]
    [InlineData(null)]
    public void A_non_platform_or_non_https_origin_is_rejected(string? origin)
    {
        Assert.False(Platform.IsPlatformHttpsOrigin(origin));
    }

    [Fact]
    public void The_platform_https_rule_follows_the_current_domain()
    {
        var moved = Platform with { BaseDomain = "taxoffix.com", ApiHost = "api.taxoffix.com" };

        Assert.True(moved.IsPlatformHttpsOrigin("https://manfer.taxoffix.com"));
        Assert.True(moved.IsPlatformHttpsOrigin("https://admin.taxoffix.com"));
        Assert.False(moved.IsPlatformHttpsOrigin("https://manfer.taxoffix.io"));
    }

    // Los correos de cobro fallido mandan al Account del landing en el mismo host al que va el CRM (www).
    [Theory]
    [InlineData("/account/plan", "https://www.taxoffix.io/account/plan")]
    [InlineData("account/plan", "https://www.taxoffix.io/account/plan")]
    public void A_landing_page_lives_on_the_www_host(string path, string expected)
    {
        Assert.Equal(expected, Platform.LandingUrl(path));
    }
}
