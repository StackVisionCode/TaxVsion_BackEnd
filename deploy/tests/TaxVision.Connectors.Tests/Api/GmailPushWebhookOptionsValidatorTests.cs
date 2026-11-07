using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using TaxVision.Connectors.Api.Options;

namespace TaxVision.Connectors.Tests.Api;

public sealed class GmailPushWebhookOptionsValidatorTests
{
    [Theory]
    [InlineData("Production", "")]
    [InlineData("Production", "   ")]
    [InlineData("Staging", "")]
    [InlineData("Testing", "")]
    public void MissingAudience_OutsideDevelopment_FailsValidation(string environment, string audience)
    {
        var result = Validate(environment, audience);

        Assert.True(result.Failed);
        Assert.Contains("ExpectedAudience", result.FailureMessage);
        Assert.Contains("CONNECTORS_GMAIL_PUSH_AUDIENCE", result.FailureMessage);
    }

    [Fact]
    public void MissingAudience_InDevelopment_Succeeds() =>
        Assert.True(Validate(Environments.Development, "").Succeeded);

    [Fact]
    public void ConfiguredAudience_InProduction_Succeeds() =>
        Assert.True(Validate(Environments.Production, "https://push.example.test/hook").Succeeded);

    private static Microsoft.Extensions.Options.ValidateOptionsResult Validate(string environment, string audience) =>
        new GmailPushWebhookOptionsValidator(new HostingEnvironment { EnvironmentName = environment }).Validate(
            null,
            new GmailPushWebhookOptions { ExpectedAudience = audience }
        );
}
