using System.Reflection;
using Microsoft.Extensions.Options;
using TaxVision.PaymentClient.Application.Abstractions.Payments;
using TaxVision.PaymentClient.Application.TenantPaymentConfigs.Queries;
using TaxVision.PaymentClient.Domain.ValueObjects;
using TaxVision.PaymentClient.Infrastructure.Providers;
using TaxVision.PaymentClient.Tests.Application;

namespace TaxVision.PaymentClient.Tests.Infrastructure;

/// <summary>El catálogo de proveedores sale de los adaptadores registrados, no del enum completo.</summary>
public sealed class PaymentProviderCatalogTests
{
    [Fact]
    public void The_catalog_lists_exactly_the_providers_with_a_registered_adapter()
    {
        var withAdapter = typeof(PaymentProviderRegistrationExtensions)
            .Assembly.GetTypes()
            .Select(type => type.GetCustomAttribute<PaymentProviderAttribute>())
            .OfType<PaymentProviderAttribute>()
            .Select(attribute => attribute.Code)
            .Order()
            .ToList();

        var catalog = InMemoryPaymentClient.RealAdapters().GetAvailableProviders();

        Assert.Equal(withAdapter, catalog.Select(descriptor => descriptor.Code).ToList());
        Assert.Contains(PaymentProviderCode.Stripe, withAdapter);
        Assert.Contains(PaymentProviderCode.PayPal, withAdapter);
    }

    [Fact]
    public void A_provider_without_adapter_has_no_descriptor()
    {
        var adapters = InMemoryPaymentClient.RealAdapters();

        Assert.Null(adapters.FindDescriptor(PaymentProviderCode.Square));
        Assert.False(adapters.IsRegistered(PaymentProviderCode.Square));
    }

    [Fact]
    public void Stripe_asks_for_publishable_and_secret_key_plus_webhook_secret_and_no_url()
    {
        var stripe = InMemoryPaymentClient.RealAdapters().FindDescriptor(PaymentProviderCode.Stripe)!;

        Assert.Equal("Stripe", stripe.DisplayName);
        Assert.Equal("Publishable key", stripe.PublicKeyLabel);
        Assert.Equal("Secret key", stripe.SecretKeyLabel);
        Assert.True(stripe.UsesWebhookSecret);
        Assert.Equal("Signing secret", stripe.WebhookSecretLabel);
        Assert.False(stripe.AcceptsApiBaseUrl);
        Assert.Null(stripe.ApiBaseUrlPlaceholder);
        Assert.Equal("https://dashboard.stripe.com/apikeys", stripe.CredentialsHelpUrl);
        // La oficina marca estos al crear el endpoint en Stripe: son los que el adaptador procesa.
        Assert.Equal(
            [
                "payment_intent.succeeded",
                "payment_intent.payment_failed",
                "payment_intent.canceled",
                "charge.refunded",
                "charge.dispute.created",
            ],
            stripe.WebhookEvents
        );
        Assert.Equal(PaymentWebhookRoutes.Stripe, stripe.WebhookRouteName);
    }

    [Fact]
    public void PayPal_asks_for_client_id_secret_and_webhook_id_with_an_optional_sandbox_url()
    {
        var payPal = InMemoryPaymentClient.RealAdapters().FindDescriptor(PaymentProviderCode.PayPal)!;

        Assert.Equal("PayPal", payPal.DisplayName);
        Assert.Equal("Client ID", payPal.PublicKeyLabel);
        Assert.Equal("Client secret", payPal.SecretKeyLabel);
        // El Webhook ID se guarda como webhook secret (opcional): sin él no se verifican reembolsos ni disputas.
        Assert.True(payPal.UsesWebhookSecret);
        Assert.Equal("Webhook ID", payPal.WebhookSecretLabel);
        Assert.True(payPal.AcceptsApiBaseUrl);
        Assert.Equal("https://api-m.sandbox.paypal.com", payPal.ApiBaseUrlPlaceholder);
        Assert.Contains("https://api-m.paypal.com", payPal.ApiBaseUrlHelp);
        Assert.Equal("https://developer.paypal.com/dashboard/applications", payPal.CredentialsHelpUrl);
        Assert.Equal(PaymentWebhookRoutes.PayPal, payPal.WebhookRouteName);
        Assert.Equal(
            ["PAYMENT.CAPTURE.REFUNDED", "PAYMENT.CAPTURE.REVERSED", "CUSTOMER.DISPUTE.CREATED"],
            payPal.WebhookEvents
        );
    }

    [Fact]
    public void The_catalog_query_returns_the_descriptors_with_the_provider_code_as_text()
    {
        var result = ListAvailablePaymentProvidersHandler.Handle(
            new ListAvailablePaymentProvidersQuery(),
            InMemoryPaymentClient.RealAdapters(),
            Options.Create(new PaymentWebhookOptions())
        );

        Assert.True(result.IsSuccess);
        var payPal = Assert.Single(result.Value, provider => provider.Code == nameof(PaymentProviderCode.PayPal));
        Assert.Equal("Client ID", payPal.PublicKeyLabel);
        Assert.True(payPal.UsesWebhookSecret);
        Assert.Equal("Webhook ID", payPal.WebhookSecretLabel);
    }
}
