using Microsoft.Extensions.Options;
using TaxVision.PaymentApp.Domain.ValueObjects;
using TaxVision.PaymentApp.Infrastructure.Providers;
using TaxVision.PaymentApp.Infrastructure.Providers.PayPal;
using TaxVision.PaymentApp.Infrastructure.Providers.Stripe;

namespace TaxVision.PaymentApp.Tests.Infrastructure;

public sealed class ProviderWebhookSecretsTests
{
    [Fact]
    public void Stripe_uses_signing_secret_and_no_webhook_id()
    {
        var secrets = CreateSecrets();

        Assert.Equal("stripe-webhook-secret", secrets.GetWebhookSecret(PaymentProviderCode.Stripe));
        Assert.Null(secrets.GetWebhookId(PaymentProviderCode.Stripe));
    }

    [Fact]
    public void PayPal_uses_webhook_id_and_no_hmac_secret()
    {
        var secrets = CreateSecrets();

        Assert.Equal("paypal-webhook-id", secrets.GetWebhookId(PaymentProviderCode.PayPal));
        Assert.Null(secrets.GetWebhookSecret(PaymentProviderCode.PayPal));
    }

    [Theory]
    [InlineData(PaymentProviderCode.Intellipay)]
    [InlineData(PaymentProviderCode.Manual)]
    [InlineData(PaymentProviderCode.Braintree)]
    public void Providers_without_public_webhook_ingress_do_not_receive_global_webhook_credentials(
        PaymentProviderCode provider
    )
    {
        var secrets = CreateSecrets();

        Assert.Null(secrets.GetWebhookSecret(provider));
        Assert.Null(secrets.GetWebhookId(provider));
    }

    private static ProviderWebhookSecrets CreateSecrets() =>
        new(
            Options.Create(
                new StripeOptions { SecretKey = "stripe-secret-key", WebhookSecret = "stripe-webhook-secret" }
            ),
            Options.Create(
                new PayPalOptions
                {
                    ClientId = "paypal-client-id",
                    ClientSecret = "paypal-client-secret",
                    WebhookId = "paypal-webhook-id",
                }
            )
        );
}
