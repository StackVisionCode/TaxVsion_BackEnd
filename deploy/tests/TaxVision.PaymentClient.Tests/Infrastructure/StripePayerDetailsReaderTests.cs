using Stripe;
using TaxVision.PaymentClient.Infrastructure.Providers.Stripe;

namespace TaxVision.PaymentClient.Tests.Infrastructure;

/// <summary>Lectura del pagador desde objetos de Stripe.net armados en memoria (sin red).</summary>
public sealed class StripePayerDetailsReaderTests
{
    [Fact]
    public void Reads_billing_details_and_card_from_the_expanded_latest_charge()
    {
        var intent = new PaymentIntent
        {
            LatestCharge = new Charge
            {
                BillingDetails = new ChargeBillingDetails { Name = "Jane Doe", Email = "jane@example.com" },
                PaymentMethodDetails = new ChargePaymentMethodDetails
                {
                    Card = new ChargePaymentMethodDetailsCard { Brand = "visa", Last4 = "4242" },
                },
            },
        };

        var payer = StripePayerDetailsReader.FromPaymentIntent(intent);

        Assert.NotNull(payer);
        Assert.Equal("Jane Doe", payer.Name);
        Assert.Equal("jane@example.com", payer.Email);
        Assert.Equal("visa", payer.CardBrand);
        Assert.Equal("4242", payer.CardLast4);
    }

    [Fact]
    public void Falls_back_to_the_expanded_payment_method_when_there_is_no_charge_yet()
    {
        var intent = new PaymentIntent
        {
            PaymentMethod = new PaymentMethod
            {
                BillingDetails = new PaymentMethodBillingDetails { Name = "Jane Doe" },
                Card = new PaymentMethodCard { Brand = "mastercard", Last4 = "4444" },
            },
        };

        var payer = StripePayerDetailsReader.FromPaymentIntent(intent);

        Assert.NotNull(payer);
        Assert.Equal("Jane Doe", payer.Name);
        Assert.Null(payer.Email);
        Assert.Equal("mastercard", payer.CardBrand);
        Assert.Equal("4444", payer.CardLast4);
    }

    [Fact]
    public void Returns_null_when_nothing_was_expanded()
    {
        Assert.Null(StripePayerDetailsReader.FromPaymentIntent(new PaymentIntent { LatestChargeId = "ch_1" }));
        Assert.Null(StripePayerDetailsReader.FromPaymentIntent(null));
    }

    [Fact]
    public void Reads_the_card_of_a_declined_charge_from_the_error()
    {
        var error = new StripeError
        {
            Code = "card_declined",
            PaymentMethod = new PaymentMethod
            {
                Card = new PaymentMethodCard { Brand = "visa", Last4 = "0002" },
            },
        };

        var payer = StripePayerDetailsReader.FromError(error);

        Assert.NotNull(payer);
        Assert.Equal("visa", payer.CardBrand);
        Assert.Equal("0002", payer.CardLast4);
        Assert.Null(StripePayerDetailsReader.FromError(new StripeError { Code = "api_error" }));
    }

    [Fact]
    public void Expands_the_charge_and_the_payment_method()
    {
        Assert.Equal(["latest_charge", "payment_method"], StripePayerDetailsReader.ExpandedFields);
    }
}
