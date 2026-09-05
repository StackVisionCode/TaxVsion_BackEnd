using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.PaymentClient.Application.Abstractions.Payments;
using TaxVision.PaymentClient.Domain.Connect;
using TaxVision.PaymentClient.Infrastructure.Providers.Stripe;

namespace TaxVision.PaymentClient.Tests.Infrastructure;

public sealed class StripeConnectGatewayTests
{
    [Fact]
    public async Task Create_account_returns_controlled_error_when_platform_secret_is_missing()
    {
        var gateway = CreateGateway(platformSecretKey: "");

        var result = await gateway.CreateAccountAsync(
            ConnectAccountType.Express,
            "owner@example.com",
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("StripeConnect.PlatformSecretKey.Missing", result.Error.Code);
    }

    [Fact]
    public async Task Webhook_verification_returns_controlled_error_when_webhook_secret_is_missing()
    {
        var gateway = CreateGateway(platformSecretKey: "");

        var result = await gateway.VerifyAndParseConnectWebhookAsync(
            "{}",
            "t=1,v1=invalid",
            webhookSecret: "",
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("StripeConnect.WebhookSecret.Missing", result.Error.Code);
    }

    private static StripeConnectGateway CreateGateway(string platformSecretKey) =>
        new(
            Options.Create(
                new PlatformStripeCredentials
                {
                    PlatformSecretKey = platformSecretKey,
                    ConnectWebhookSecret = string.Empty,
                }
            ),
            NullLogger<StripeConnectGateway>.Instance
        );
}
