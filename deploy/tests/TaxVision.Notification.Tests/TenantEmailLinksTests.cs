using TaxVision.Notification.Application.Common;

namespace TaxVision.Notification.Tests;

public class TenantEmailLinksTests
{
    [Fact]
    public void SigningLink_uses_office_subdomain_when_host_resolves()
    {
        var portal = new PortalOptions { BaseUrl = "https://app.test" };

        var link = TenantEmailLinks.SigningLink("manfer.taxproffice.com", portal, "tok123");

        Assert.Equal("https://manfer.taxproffice.com/signature/public/tok123", link);
    }

    [Fact]
    public void SigningLink_falls_back_to_fixed_base_when_host_is_null()
    {
        var portal = new PortalOptions { BaseUrl = "https://app.test" };

        var link = TenantEmailLinks.SigningLink(null, portal, "tok123");

        Assert.Equal("https://app.test/signature/public/tok123", link);
    }

    [Fact]
    public void Required_bases_use_only_the_office_host()
    {
        var portal = new PortalOptions
        {
            BaseUrl = "https://app.test",
            ClientBaseUrl = "https://client.test",
            ClientPathPrefix = "/portal",
        };

        Assert.Equal("https://manfer.taxproffice.com", TenantEmailLinks.RequiredStaffBase("manfer.taxproffice.com"));
        Assert.Equal(
            "https://manfer.taxproffice.com/portal",
            TenantEmailLinks.RequiredClientBase("manfer.taxproffice.com", portal)
        );
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public void Required_bases_never_fall_back_to_the_fixed_base(string? host)
    {
        var portal = new PortalOptions { BaseUrl = "https://app.test", ClientBaseUrl = "https://client.test" };

        Assert.ThrowsAny<ArgumentException>(() => TenantEmailLinks.RequiredStaffBase(host!));
        Assert.ThrowsAny<ArgumentException>(() => TenantEmailLinks.RequiredClientBase(host!, portal));
    }

    [Fact]
    public void PublicShareDownloadLink_uses_office_subdomain_and_escapes_email()
    {
        var portal = new PortalOptions { BaseUrl = "https://app.test" };

        var link = TenantEmailLinks.PublicShareDownloadLink(
            "manfer.taxproffice.com",
            portal,
            "shr123",
            "signer+tag@example.com"
        );

        Assert.Equal("https://manfer.taxproffice.com/storage/public/shr123?email=signer%2Btag%40example.com", link);
    }

    [Fact]
    public void PublicShareDownloadLink_falls_back_to_fixed_base_when_host_is_null()
    {
        var portal = new PortalOptions { BaseUrl = "https://app.test" };

        var link = TenantEmailLinks.PublicShareDownloadLink(null, portal, "shr123", "a@b.com");

        Assert.Equal("https://app.test/storage/public/shr123?email=a%40b.com", link);
    }
}
