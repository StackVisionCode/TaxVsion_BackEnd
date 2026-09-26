using TaxVision.PaymentApp.Application.SaaSPayments.Common;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Tests.Application;

public sealed class SaaSPaymentChargeOutcomeTests
{
    [Fact]
    public void ComputeNextRetryAtUtc_for_a_plan_change_charge_never_schedules_a_retry()
    {
        // Regression: un upgrade de plan es un cargo interactivo iniciado por el usuario, no
        // dunning en background — debe fallar rápido (WillRetry=false) incluso en el primer
        // intento, a diferencia de SubscriptionRenewal que reintenta 1h/6h/24h.
        var payment = CreatePayment(SaaSPaymentType.PlanChangeCharge);

        var nextRetryAtUtc = SaaSPaymentChargeOutcome.ComputeNextRetryAtUtc(payment, DateTime.UtcNow);

        Assert.Null(nextRetryAtUtc);
    }

    [Fact]
    public void ComputeNextRetryAtUtc_for_a_subscription_renewal_schedules_a_retry_on_the_first_attempt()
    {
        var payment = CreatePayment(SaaSPaymentType.SubscriptionRenewal);

        var nextRetryAtUtc = SaaSPaymentChargeOutcome.ComputeNextRetryAtUtc(payment, DateTime.UtcNow);

        Assert.NotNull(nextRetryAtUtc);
    }

    [Theory]
    [InlineData(SaaSPaymentType.SeatsPurchaseCharge)]
    [InlineData(SaaSPaymentType.SubscriptionRenewalCheckout)]
    public void Hosted_checkouts_never_get_dunning(SaaSPaymentType type)
    {
        var payment = CreatePayment(type);

        Assert.False(SaaSPaymentChargeOutcome.SupportsDunning(type));
        Assert.Null(SaaSPaymentChargeOutcome.ComputeNextRetryAtUtc(payment, DateTime.UtcNow));
    }

    [Fact]
    public void A_late_failure_gets_the_same_first_retry_as_an_immediate_failure()
    {
        var payment = CreatePayment(SaaSPaymentType.SeatRenewal);
        var nowUtc = DateTime.UtcNow;
        var immediate = SaaSPaymentChargeOutcome.ComputeNextRetryAtUtc(payment, nowUtc);

        payment.MarkProcessing(
            ExternalPaymentReference.Create(PaymentProviderCode.Stripe, "pi_late").Value,
            "processing",
            providerResponseBody: null,
            Guid.Empty,
            nowUtc
        );
        var late = SaaSPaymentChargeOutcome.ComputeNextRetryAtUtc(payment, nowUtc, failedAttemptRecorded: true);

        Assert.Equal(nowUtc.AddHours(1), immediate);
        Assert.Equal(immediate, late);
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
