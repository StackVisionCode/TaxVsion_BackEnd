using TaxVision.Tenant.Domain.Enums;

namespace TaxVision.Tenant.Tests.Domain;

public sealed class TenantDomainTests
{
    [Fact]
    public void Creation_normalizes_subdomain_and_timezone()
    {
        var result = TaxVision.Tenant.Domain.Tenant.Create(" Demo Tax Office ", "Demo-Office", "America/New_York");

        Assert.True(result.IsSuccess);
        Assert.Equal("Demo Tax Office", result.Value.Name);
        Assert.Equal("demo-office", result.Value.SubDomain);
        Assert.Equal(EnumTenantStatus.TenantStatus.Active, result.Value.Status);
    }

    [Theory]
    [InlineData("ab")]
    [InlineData("UPPER_CASE")]
    [InlineData("spaces are invalid")]
    public void Creation_rejects_invalid_subdomains(string subdomain)
    {
        var result = TaxVision.Tenant.Domain.Tenant.Create("Demo", subdomain, "America/New_York");

        Assert.True(result.IsFailure);
        Assert.Equal("Tenant.Subdomain", result.Error.Code);
    }

    [Fact]
    public void Only_active_tenant_can_be_suspended()
    {
        var tenant = TaxVision.Tenant.Domain.Tenant.Create("Demo", "demo-office", "America/New_York").Value;

        Assert.True(tenant.Suspend().IsSuccess);
        Assert.True(tenant.Suspend().IsFailure);
    }
}
