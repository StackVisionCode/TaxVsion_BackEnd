using BuildingBlocks.Security;
using Xunit;

namespace TaxVision.BuildingBlocks.Tests.Security;

public sealed class SensitiveNotificationContentTests
{
    [Theory]
    [InlineData("auth.otp_code")]
    [InlineData("onboarding.otp_code")]
    [InlineData("sig.verification-challenge.v1")]
    public void Sensitive_templates_are_redacted_for_storage(string templateKey)
    {
        Assert.Equal(
            SensitiveNotificationContent.Redacted,
            SensitiveNotificationContent.ForStorage(templateKey, "123456")
        );
    }

    [Fact]
    public void Non_sensitive_content_is_preserved()
    {
        Assert.Equal("Invoice ready", SensitiveNotificationContent.ForStorage("invoice.ready", "Invoice ready"));
    }
}
