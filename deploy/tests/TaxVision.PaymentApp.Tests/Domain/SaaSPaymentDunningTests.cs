using TaxVision.PaymentApp.Application.SaaSPayments.Common;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Tests.Domain;

/// <summary>El reintento automático de una renovación tiene que terminar, aunque el cobro falle antes del provider.</summary>
public sealed class SaaSPaymentDunningTests
{
    [Fact]
    public void A_failure_before_reaching_the_provider_counts_as_an_attempt()
    {
        var payment = CreateRenewalPayment();

        payment.MarkFailed(
            "card_declined",
            "Declined.",
            true,
            DateTime.UtcNow.AddHours(1),
            Guid.Empty,
            DateTime.UtcNow
        );

        Assert.Single(payment.Attempts);
    }

    [Fact]
    public void Dunning_gives_up_after_the_third_retry_even_if_the_provider_is_never_reached()
    {
        var payment = CreateRenewalPayment();
        var nowUtc = DateTime.UtcNow;
        var retries = 0;

        // Antes un fallo desde Pending no sumaba intento y esto reintentaba cada hora para siempre.
        while (true)
        {
            var nextRetryAtUtc = SaaSPaymentChargeOutcome.ComputeNextRetryAtUtc(payment, nowUtc);
            payment.MarkFailed(
                "card_declined",
                "Declined.",
                nextRetryAtUtc is not null,
                nextRetryAtUtc,
                Guid.Empty,
                nowUtc
            );
            if (payment.NextRetryAtUtc is null)
                break;

            nowUtc = payment.NextRetryAtUtc.Value;
            Assert.True(payment.PrepareForRetry(Guid.Empty, nowUtc).IsSuccess);
            retries++;
            Assert.True(retries <= 3, "Dunning never stopped.");
        }

        Assert.Equal(3, retries);
        Assert.Equal(4, payment.Attempts.Count);
        Assert.True(payment.PrepareForRetry(Guid.Empty, nowUtc.AddDays(2)).IsFailure);
    }

    [Fact]
    public void Each_retry_sends_a_new_key_to_the_provider()
    {
        var payment = CreateRenewalPayment();
        var first = payment.ProviderIdempotencyKeyForNextAttempt();

        payment.MarkFailed("card_declined", "Declined.", true, DateTime.UtcNow, Guid.Empty, DateTime.UtcNow);
        var second = payment.ProviderIdempotencyKeyForNextAttempt();

        Assert.Equal(payment.IdempotencyKey, first);
        Assert.Equal($"{payment.IdempotencyKey.Value}-attempt-2", second.Value);
    }

    private static SaaSPayment CreateRenewalPayment() =>
        SaaSPayment
            .Create(
                Guid.NewGuid(),
                IdempotencyKey.Create("subscription-renewal-key").Value,
                Money.Create(2900, "USD").Value,
                SaaSPaymentType.SubscriptionRenewal,
                Guid.NewGuid(),
                PaymentProviderCode.Stripe,
                StatementDescriptor.Create("TAXVISION SAAS").Value,
                Guid.Empty,
                DateTime.UtcNow
            )
            .Value;
}
