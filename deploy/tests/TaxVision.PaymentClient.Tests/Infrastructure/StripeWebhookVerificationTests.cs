using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Stripe;
using TaxVision.PaymentClient.Application.Abstractions.Payments;
using TaxVision.PaymentClient.Infrastructure.Providers.Stripe;

namespace TaxVision.PaymentClient.Tests.Infrastructure;

/// <summary>Stripe con el contrato de encabezados: sigue verificando <c>Stripe-Signature</c> (HMAC) contra el
/// webhook secret de la oficina.</summary>
public sealed class StripeWebhookVerificationTests
{
    private const string Secret = "whsec_test_office";

    private static readonly string Payload =
        """{"id":"evt_1","object":"event","type":"charge.refunded","api_version":"""
        + $"\"{StripeConfiguration.ApiVersion}\""
        + ""","created":1762171200,"livemode":false,"pending_webhooks":1,"request":{"id":null,"idempotency_key":null},"data":{"object":{"id":"ch_1","object":"charge","amount_refunded":0}}}""";

    private static readonly TenantProviderCredentials Credentials = new("sk_test_x", Secret);

    [Fact]
    public async Task A_valid_Stripe_signature_is_accepted()
    {
        var headers = new Dictionary<string, string> { ["stripe-signature"] = Sign(Payload, Secret) };

        var result = await Adapter().VerifyWebhookSignatureAsync(Payload, headers, Credentials, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("evt_1", result.Value.ProviderEventId);
        Assert.Equal("charge.refunded", result.Value.EventType);
        Assert.Equal(headers["stripe-signature"], result.Value.Signature);
    }

    [Fact]
    public async Task A_signature_made_with_another_secret_is_rejected()
    {
        var headers = new Dictionary<string, string> { ["Stripe-Signature"] = Sign(Payload, "whsec_other") };

        var result = await Adapter().VerifyWebhookSignatureAsync(Payload, headers, Credentials, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("Stripe.WebhookSignature.Invalid", result.Error.Code);
    }

    private static StripePaymentAdapter Adapter() => new(NullLogger<StripePaymentAdapter>.Instance);

    // Formato de Stripe: t=<unix>,v1=HMAC-SHA256(secret, "<t>.<payload>").
    private static string Sign(string payload, string secret)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var hash = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(secret),
            Encoding.UTF8.GetBytes($"{timestamp}.{payload}")
        );
        return $"t={timestamp},v1={Convert.ToHexStringLower(hash)}";
    }
}
