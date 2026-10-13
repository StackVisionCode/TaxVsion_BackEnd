using TaxVision.Tenant.Domain.Platform;

namespace TaxVision.Tenant.Tests.Domain;

public sealed class PlatformSettingsTests
{
    private static readonly DateTime Now = new(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc);

    private static PlatformSettings Valid() =>
        PlatformSettings
            .Create(
                "TaxOffix",
                "TaxOffix LLC",
                "taxoffix.io",
                "api.taxoffix.io",
                "support@taxoffix.io",
                "billing@taxoffix.io",
                Now
            )
            .Value;

    [Fact]
    public void Create_starts_at_version_1_on_the_fixed_singleton_id()
    {
        var settings = Valid();

        Assert.Equal(PlatformSettings.SingletonId, settings.Id);
        Assert.Equal(1, settings.Version);
        Assert.Equal("taxoffix.io", settings.BaseDomain);
        Assert.Null(settings.UpdatedByUserId);
    }

    [Fact]
    public void Create_normalizes_hosts_and_emails()
    {
        var settings = PlatformSettings
            .Create(
                " TaxOffix ",
                "TaxOffix LLC",
                " TaxOffix.IO. ",
                "API.TaxOffix.io",
                " Support@TaxOffix.io ",
                "Billing@TaxOffix.io",
                Now
            )
            .Value;

        Assert.Equal("TaxOffix", settings.BrandName);
        Assert.Equal("taxoffix.io", settings.BaseDomain);
        Assert.Equal("api.taxoffix.io", settings.ApiHost);
        Assert.Equal("support@taxoffix.io", settings.SupportEmail);
        Assert.Equal("billing@taxoffix.io", settings.BillingEmail);
    }

    [Theory]
    [InlineData("https://taxoffix.io")]
    [InlineData("taxoffix.io/path")]
    [InlineData("localhost")]
    [InlineData("tax offix.io")]
    [InlineData("")]
    public void Create_rejects_a_base_domain_that_is_not_a_bare_domain(string baseDomain)
    {
        var result = PlatformSettings.Create(
            "TaxOffix",
            "TaxOffix LLC",
            baseDomain,
            "api.taxoffix.io",
            "support@taxoffix.io",
            "billing@taxoffix.io",
            Now
        );

        Assert.True(result.IsFailure);
        Assert.Equal("PlatformSettings.BaseDomain", result.Error.Code);
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("support@")]
    [InlineData("")]
    public void Create_rejects_an_invalid_support_email(string email)
    {
        var result = PlatformSettings.Create(
            "TaxOffix",
            "TaxOffix LLC",
            "taxoffix.io",
            "api.taxoffix.io",
            email,
            "billing@taxoffix.io",
            Now
        );

        Assert.True(result.IsFailure);
        Assert.Equal("PlatformSettings.SupportEmail", result.Error.Code);
    }

    [Fact]
    public void Create_rejects_a_one_letter_brand()
    {
        var result = PlatformSettings.Create(
            "T",
            "TaxOffix LLC",
            "taxoffix.io",
            "api.taxoffix.io",
            "support@taxoffix.io",
            "billing@taxoffix.io",
            Now
        );

        Assert.True(result.IsFailure);
        Assert.Equal("PlatformSettings.BrandName", result.Error.Code);
    }

    [Fact]
    public void Update_with_new_values_bumps_the_version_and_records_who()
    {
        var settings = Valid();
        var actor = Guid.NewGuid();

        var result = settings.Update(
            "TaxOffix Pro",
            "TaxOffix LLC",
            "taxoffix.com",
            "api.taxoffix.com",
            "support@taxoffix.com",
            "billing@taxoffix.com",
            actor,
            Now.AddDays(1)
        );

        Assert.True(result.Value);
        Assert.Equal(2, settings.Version);
        Assert.Equal("taxoffix.com", settings.BaseDomain);
        Assert.Equal(actor, settings.UpdatedByUserId);
        Assert.Equal(Now.AddDays(1), settings.UpdatedAtUtc);
    }

    [Fact]
    public void Update_with_the_same_values_changes_nothing()
    {
        var settings = Valid();

        var result = settings.Update(
            "TaxOffix",
            "TaxOffix LLC",
            "TAXOFFIX.io",
            "api.taxoffix.io",
            "support@taxoffix.io",
            "billing@taxoffix.io",
            Guid.NewGuid(),
            Now.AddDays(1)
        );

        Assert.False(result.Value);
        Assert.Equal(1, settings.Version);
        Assert.Equal(Now, settings.UpdatedAtUtc);
    }

    [Fact]
    public void Update_with_an_invalid_value_leaves_the_settings_untouched()
    {
        var settings = Valid();

        var result = settings.Update(
            "TaxOffix",
            "TaxOffix LLC",
            "taxoffix.io",
            "not a host",
            "support@taxoffix.io",
            "billing@taxoffix.io",
            Guid.NewGuid(),
            Now.AddDays(1)
        );

        Assert.True(result.IsFailure);
        Assert.Equal("PlatformSettings.ApiHost", result.Error.Code);
        Assert.Equal("api.taxoffix.io", settings.ApiHost);
        Assert.Equal(1, settings.Version);
    }
}
