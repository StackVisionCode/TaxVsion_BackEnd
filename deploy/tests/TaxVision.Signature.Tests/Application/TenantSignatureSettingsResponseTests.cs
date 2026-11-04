using TaxVision.Signature.Application.Settings;
using TaxVision.Signature.Domain.Settings;

namespace TaxVision.Signature.Tests.Application;

public sealed class TenantSignatureSettingsResponseTests
{
    [Fact]
    public void Response_exposes_send_certificate_and_consent_defaults()
    {
        var settings = TenantSignatureSettings.CreateForNewTenant(Guid.NewGuid(), "audit-secret").Value;
        settings.SetSendCertificateToSignersDefault(true);
        settings.SetRequiresConsentByDefault(false);

        var response = TenantSignatureSettingsResponse.From(
            settings,
            settings.EffectiveWithin(TestSignatureLimits.Default)
        );

        Assert.True(response.SendCertificateToSignersDefault);
        Assert.False(response.RequiresConsentByDefault);
    }

    [Fact]
    public void Response_exposes_the_effective_retention_and_where_it_comes_from()
    {
        var settings = TenantSignatureSettings.CreateForNewTenant(Guid.NewGuid(), "audit-secret").Value;
        var before = TenantSignatureSettingsResponse.From(
            settings,
            settings.EffectiveWithin(TestSignatureLimits.Default)
        );

        settings.ApplyOfficeRetention(12, DateTime.UtcNow);
        var after = TenantSignatureSettingsResponse.From(
            settings,
            settings.EffectiveWithin(TestSignatureLimits.Default)
        );

        Assert.Equal(RetentionPolicy.DefaultRetentionYears, before.RetentionYears);
        Assert.Equal("Signature", before.RetentionYearsSource);
        Assert.Equal(12, after.RetentionYears);
        Assert.Equal("Office", after.RetentionYearsSource);
    }
}
