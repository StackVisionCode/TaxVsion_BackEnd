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

    private static SaaSPayment CreatePayment(SaaSPaymentType type) =>
        SaaSPayment.Create(
            Guid.NewGuid(),
            IdempotencyKey.Create("key-1").Value,
            Money.Create(1999, "USD").Value,
            type,
            Guid.NewGuid(),
            PaymentProviderCode.Stripe,
            StatementDescriptor.Create("TAXVISION SAAS").Value,
            Guid.Empty,
            DateTime.UtcNow).Value;
}
