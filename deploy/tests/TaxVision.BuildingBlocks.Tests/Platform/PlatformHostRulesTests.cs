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
}
