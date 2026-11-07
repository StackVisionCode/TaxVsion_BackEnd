using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Tests.Domain;

/// <summary>Los casos de <c>SaaSPaymentChargeOutcomeTests</c>, movidos con la política al dominio (PAY-7).</summary>
public sealed class DunningPolicyTests
{
    private static readonly DateTime NowUtc = new(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(SaaSPaymentType.SubscriptionRenewal)]
    [InlineData(SaaSPaymentType.SeatRenewal)]
    [InlineData(SaaSPaymentType.AddOnRenewal)]
    public void NextRetry_follows_schedule_and_stops_after_max_attempts(SaaSPaymentType type)
    {
        Assert.Equal(NowUtc.AddHours(1), DunningPolicy.Decide(type, 0, NowUtc).NextRetryAtUtc);
        Assert.Equal(NowUtc.AddHours(6), DunningPolicy.Decide(type, 1, NowUtc).NextRetryAtUtc);
        Assert.Equal(NowUtc.AddHours(24), DunningPolicy.Decide(type, 2, NowUtc).NextRetryAtUtc);
        Assert.False(DunningPolicy.Decide(type, DunningPolicy.MaxAttempts - 1, NowUtc).WillRetry);
        Assert.False(DunningPolicy.Decide(type, DunningPolicy.MaxAttempts, NowUtc).WillRetry);
    }

    [Fact]
    public void A_plan_change_charge_never_schedules_a_retry()
    {
        // Regression: un upgrade de plan es un cargo interactivo iniciado por el usuario, no
        // dunning en background — debe fallar rápido (WillRetry=false) incluso en el primer
        // intento, a diferencia de SubscriptionRenewal que reintenta 1h/6h/24h.
        var payment = CreatePayment(SaaSPaymentType.PlanChangeCharge);

        Assert.False(DunningPolicy.Decide(payment, NowUtc).WillRetry);
    }

    [Fact]
    public void A_subscription_renewal_schedules_a_retry_on_the_first_attempt()
    {
        var payment = CreatePayment(SaaSPaymentType.SubscriptionRenewal);

        Assert.True(DunningPolicy.Decide(payment, NowUtc).WillRetry);
    }

    [Theory]
    [InlineData(SaaSPaymentType.SeatsPurchaseCharge)]
    [InlineData(SaaSPaymentType.SubscriptionRenewalCheckout)]
    public void Hosted_checkouts_never_get_dunning(SaaSPaymentType type)
    {
        var payment = CreatePayment(type);

        Assert.False(DunningPolicy.SupportsDunning(type));
        Assert.False(DunningPolicy.Decide(payment, NowUtc).WillRetry);
    }

    [Fact]
    public void A_late_failure_gets_the_same_first_retry_as_an_immediate_failure()
    {
        var payment = CreatePayment(SaaSPaymentType.SeatRenewal);
        var immediate = DunningPolicy.Decide(payment, NowUtc);

        payment.MarkProcessing(
            ExternalPaymentReference.Create(PaymentProviderCode.Stripe, "pi_late").Value,
            "processing",
            providerResponseBody: null,
            Guid.Empty,
            NowUtc
        );
        var late = DunningPolicy.Decide(payment, NowUtc, failedAttemptRecorded: true);

        Assert.Equal(NowUtc.AddHours(1), immediate.NextRetryAtUtc);
        Assert.Equal(immediate, late);
    }

    // El webhook descuenta el intento fallido: sin intentos previos da -1 y no agenda nada.
    [Fact]
    public void A_late_failure_with_no_attempt_recorded_schedules_nothing()
    {
        var payment = CreatePayment(SaaSPaymentType.SubscriptionRenewal);

        Assert.False(DunningPolicy.Decide(payment, NowUtc, failedAttemptRecorded: true).WillRetry);
    }

    [Theory]
    [InlineData("requires_payment_method")]
    [InlineData("REQUIRES_PAYMENT_METHOD")]
    [InlineData("PayPal.Authorize.NotImplemented")]
    public void Permanent_failures_never_retry(string code)
    {
        Assert.True(DunningPolicy.IsPermanentFailure(code));
        Assert.False(DunningPolicy.ForFailure(code, DunningDecision.RetryAt(NowUtc)).WillRetry);
    }

    [Fact]
    public void Other_failures_keep_the_scheduled_retry()
    {
        var scheduled = DunningDecision.RetryAt(NowUtc.AddHours(1));

        Assert.False(DunningPolicy.IsPermanentFailure(null));
        Assert.Equal(scheduled, DunningPolicy.ForFailure("card_declined", scheduled));
    }

    [Fact]
    public void MarkFailed_stores_what_the_policy_decided()
    {
        var payment = CreatePayment(SaaSPaymentType.SubscriptionRenewal);

        Assert.True(
            payment
                .MarkFailed("card_declined", "Declined.", DunningDecision.RetryAt(NowUtc), Guid.Empty, NowUtc)
                .IsSuccess
        );

        Assert.Equal(NowUtc, payment.NextRetryAtUtc);
    }

    private static SaaSPayment CreatePayment(SaaSPaymentType type) =>
        SaaSPayment
            .Create(
                Guid.NewGuid(),
                IdempotencyKey.Create("key-1").Value,
                Money.Create(1999, "USD").Value,
                type,
                Guid.NewGuid(),
                PaymentProviderCode.Stripe,
                StatementDescriptor.Create("TAXVISION SAAS").Value,
                Guid.Empty,
                DateTime.UtcNow
            )
            .Value;
}
