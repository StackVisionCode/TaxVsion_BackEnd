using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.PaymentClient.Application.Abstractions.Payments;
using TaxVision.PaymentClient.Infrastructure.Providers.PayPal;
using TaxVision.PaymentClient.Infrastructure.Providers.Stripe;

namespace TaxVision.PaymentClient.Tests.Infrastructure;

/// <summary>
/// Cada adaptador dice cómo lo ve quien paga: el procesador de tarjetas (Stripe) es "una tarjeta" y nunca se
/// nombra; una billetera (PayPal) se nombra por su marca. El checkout arma los textos con esto, sin conocer
/// proveedores.
/// </summary>
public sealed class PaymentProviderPayerMethodTests
{
    [Fact]
    public void Stripe_is_a_card_for_the_payer()
    {
        var descriptor = new StripePaymentAdapter(NullLogger<StripePaymentAdapter>.Instance).Descriptor;

        Assert.Equal(PayerMethodKind.Card, descriptor.PayerMethod);
        Assert.Equal("Credit or debit card", descriptor.PayerLabel);
    }

    [Fact]
    public void PayPal_is_a_wallet_named_by_its_brand()
    {
        var descriptor = new PayPalPaymentAdapter(
            new StubHttpClientFactory(new HttpClientHandler()),
            NullLogger<PayPalPaymentAdapter>.Instance
        ).Descriptor;

        Assert.Equal(PayerMethodKind.Wallet, descriptor.PayerMethod);
        Assert.Equal("PayPal", descriptor.PayerLabel);
    }
}
