using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Stripe;
using TaxVision.PaymentClient.Application.Abstractions.Payments;
using TaxVision.PaymentClient.Domain.Connect;
using TaxVision.PaymentClient.Domain.TenantPayments;
using TaxVision.PaymentClient.Infrastructure.Providers.Stripe;

namespace TaxVision.PaymentClient.Tests.Infrastructure;

public sealed class StripeConnectGatewayTests
{
    private const string Secret = "whsec_connect";

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

    [Theory]
    [InlineData("payment_intent.succeeded", "succeeded", PaymentStatus.Succeeded, 2500L)]
    [InlineData("payment_intent.payment_failed", "requires_payment_method", PaymentStatus.Failed, null)]
    public async Task Charge_event_of_a_connected_account_is_parsed(
        string eventType,
        string intentStatus,
        PaymentStatus expected,
        long? paidCents
    )
    {
        var payload = ChargeEvent(eventType, intentStatus, account: "acct_office");

        var result = await CreateGateway("sk_unused")
            .VerifyAndParseConnectWebhookAsync(payload, Sign(payload), Secret, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("acct_office", result.Value.StripeConnectAccountId);
        Assert.Equal("pi_office", result.Value.ChargeUpdate!.ProviderChargeReference);
        Assert.Equal(expected, result.Value.ChargeUpdate.Status);
        Assert.Equal(paidCents, result.Value.ChargeUpdate.PaidAmountCents);
    }

    [Fact]
    public async Task Charge_event_without_a_connected_account_is_rejected()
    {
        var payload = ChargeEvent("payment_intent.succeeded", "succeeded", account: null);

        var result = await CreateGateway("sk_unused")
            .VerifyAndParseConnectWebhookAsync(payload, Sign(payload), Secret, CancellationToken.None);

        Assert.Equal("StripeConnect.Webhook.MissingAccount", result.Error.Code);
    }

    private static string ChargeEvent(string eventType, string intentStatus, string? account)
    {
        var accountField = account is null ? "" : $"\"account\":\"{account}\",";
        return "{\"id\":\"evt_connect\",\"object\":\"event\",\"type\":\""
            + eventType
            + "\",\"api_version\":\""
            + StripeConfiguration.ApiVersion
            + "\",\"created\":1762171200,\"livemode\":false,\"pending_webhooks\":1,"
            + accountField
            + "\"request\":{\"id\":null,\"idempotency_key\":null},\"data\":{\"object\":{\"id\":\"pi_office\","
            + "\"object\":\"payment_intent\",\"amount\":2500,\"currency\":\"usd\",\"status\":\""
            + intentStatus
            + "\"}}}";
    }

    // Formato de Stripe: t=<unix>,v1=HMAC-SHA256(secret, "<t>.<payload>").
    private static string Sign(string payload)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var hash = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(Secret),
            Encoding.UTF8.GetBytes($"{timestamp}.{payload}")
        );
        return $"t={timestamp},v1={Convert.ToHexStringLower(hash)}";
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
