using Stripe;
using TaxVision.PaymentClient.Domain.TenantPayments;
using TaxVision.PaymentClient.Infrastructure.Providers.Stripe;

namespace TaxVision.PaymentClient.Tests.Infrastructure;

/// <summary>Estado de un PaymentIntent (retrieve) → payload canónico, sin red.</summary>
public sealed class StripeChargeStatusMappingTests
{
    [Fact]
    public void Succeeded_reports_the_amount_received_for_the_amount_check()
    {
        var payload = StripePaymentAdapter.MapIntentStatus(
            new PaymentIntent
            {
                Id = "pi_1",
                Status = "succeeded",
                Amount = 2500,
                AmountReceived = 2500,
                Currency = "usd",
            }
        );

        Assert.Equal(PaymentStatus.Succeeded, payload.Status);
        Assert.Equal("pi_1", payload.ProviderChargeReference);
        Assert.Equal(2500, payload.PaidAmountCents);
        Assert.Equal("usd", payload.PaidCurrency);
    }

    [Fact]
    public void Canceled_maps_to_Cancelled()
    {
        var payload = StripePaymentAdapter.MapIntentStatus(new PaymentIntent { Id = "pi_1", Status = "canceled" });

        Assert.Equal(PaymentStatus.Cancelled, payload.Status);
    }

    [Fact]
    public void A_failed_attempt_back_in_requires_payment_method_maps_to_Failed_with_the_error()
    {
        var payload = StripePaymentAdapter.MapIntentStatus(
            new PaymentIntent
            {
                Id = "pi_1",
                Status = "requires_payment_method",
                LastPaymentError = new StripeError { Code = "card_declined", Message = "Your card was declined." },
            }
        );

        Assert.Equal(PaymentStatus.Failed, payload.Status);
        Assert.Equal("card_declined", payload.FailureCode);
        Assert.Equal("Your card was declined.", payload.FailureMessage);
    }

    [Theory]
    [InlineData("requires_payment_method", PaymentStatus.Processing)]
    [InlineData("processing", PaymentStatus.Processing)]
    [InlineData("requires_confirmation", PaymentStatus.Processing)]
    [InlineData("requires_action", PaymentStatus.RequiresAction)]
    public void Anything_Stripe_has_not_resolved_stays_pending(string status, PaymentStatus expected)
    {
        // requires_payment_method sin error del intento no es un fallo: no se inventa.
        var payload = StripePaymentAdapter.MapIntentStatus(new PaymentIntent { Id = "pi_1", Status = status });

        Assert.Equal(expected, payload.Status);
        Assert.Null(payload.PaidAmountCents);
    }
}
