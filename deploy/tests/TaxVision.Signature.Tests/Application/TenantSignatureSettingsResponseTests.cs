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

        var response = TenantSignatureSettingsResponse.From(settings);

        Assert.True(response.SendCertificateToSignersDefault);
        Assert.False(response.RequiresConsentByDefault);
    }
}
