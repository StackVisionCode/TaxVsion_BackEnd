using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Tests.Domain;

public sealed class SaaSPaymentTests
{
    [Fact]
    public void Create_with_a_zero_amount_fails()
    {
        var result = SaaSPayment.Create(
            Guid.NewGuid(), IdempotencyKey.Create("key-1").Value, Money.Zero("USD"), SaaSPaymentType.SubscriptionRenewal,
            Guid.NewGuid(), PaymentProviderCode.Stripe, StatementDescriptor.Create("TAXVISION SAAS").Value, Guid.Empty, DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal("SaaSPayment.InvalidAmount", result.Error.Code);
    }

    [Fact]
    public void Create_starts_in_Pending_status()
    {
        var payment = CreatePendingPayment();

        Assert.Equal(PaymentStatus.Pending, payment.Status);
        Assert.Empty(payment.Attempts);
        Assert.Empty(payment.Refunds);
    }

    [Fact]
    public void MarkProcessing_then_MarkSucceeded_reaches_Succeeded_and_records_an_attempt()
    {
        var payment = CreatePendingPayment();
        var reference = ExternalPaymentReference.Create(PaymentProviderCode.Stripe, "pi_123").Value;
        var nowUtc = DateTime.UtcNow;

        var processing = payment.MarkProcessing(reference, "200", null, Guid.Empty, nowUtc);
        var succeeded = payment.MarkSucceeded(nowUtc, Guid.Empty);

        Assert.True(processing.IsSuccess);
        Assert.True(succeeded.IsSuccess);
        Assert.Equal(PaymentStatus.Succeeded, payment.Status);
        Assert.Single(payment.Attempts);
        Assert.Equal(reference, payment.ExternalChargeReference);
    }

    [Fact]
    public void MarkFailed_from_Pending_succeeds_and_schedules_a_retry()
    {
        var payment = CreatePendingPayment();
        var nowUtc = DateTime.UtcNow;
        var nextRetry = nowUtc.AddHours(1);

        var result = payment.MarkFailed("card_declined", "The card was declined.", willRetry: true, nextRetry, Guid.Empty, nowUtc);

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentStatus.Failed, payment.Status);
        Assert.Equal(nextRetry, payment.NextRetryAtUtc);
    }

    [Fact]
    public void MarkFailed_without_retry_leaves_no_scheduled_retry()
    {
        // Ruta que usan el webhook handler y el job de reconciliación: el provider ya
        // confirmó el fallo out-of-band, así que no tiene sentido reintentar solos.
        var payment = CreatePendingPayment();

        var result = payment.MarkFailed("card_declined", "The card was declined.", willRetry: false, nextRetryAtUtc: null, Guid.Empty, DateTime.UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentStatus.Failed, payment.Status);
        Assert.Null(payment.NextRetryAtUtc);
    }

    [Fact]
    public void MarkFailed_after_Succeeded_is_rejected()
    {
        var payment = CreateSucceededPayment();

        var result = payment.MarkFailed("card_declined", "reason", willRetry: false, null, Guid.Empty, DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal("SaaSPayment.InvalidTransition", result.Error.Code);
        Assert.Equal(PaymentStatus.Succeeded, payment.Status);
    }

    [Fact]
    public void RefundPartial_twice_up_to_the_full_amount_reaches_Refunded()
    {
        var payment = CreateSucceededPayment(); // 1999 cents
        var nowUtc = DateTime.UtcNow;

        var first = payment.RefundPartial(Money.Create(999, "USD").Value, "customer request", Guid.Empty, nowUtc);
        Assert.True(first.IsSuccess);
        Assert.Equal(PaymentStatus.PartiallyRefunded, payment.Status);

        var second = payment.RefundPartial(Money.Create(1000, "USD").Value, "customer request", Guid.Empty, nowUtc);
        Assert.True(second.IsSuccess);
        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.Equal(2, payment.Refunds.Count);
    }

    [Fact]
    public void RefundPartial_exceeding_the_original_amount_fails()
    {
        var payment = CreateSucceededPayment(); // 1999 cents

        var result = payment.RefundPartial(Money.Create(2000, "USD").Value, "customer request", Guid.Empty, DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal("SaaSPayment.RefundExceedsPrincipal", result.Error.Code);
    }

    [Fact]
    public void RefundFull_refunds_exactly_the_remaining_balance()
    {
        var payment = CreateSucceededPayment(); // 1999 cents

        var result = payment.RefundFull("customer request", Guid.Empty, DateTime.UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.Equal(1999, payment.Refunds.Single().Amount.AmountCents);
    }

    [Fact]
    public void RefundPartial_on_a_legally_held_payment_is_rejected()
    {
        var payment = CreateSucceededPayment();
        payment.SetLegalHold(true, Guid.Empty, DateTime.UtcNow);

        var result = payment.RefundPartial(Money.Create(100, "USD").Value, "reason", Guid.Empty, DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal("Payment.LegalHeld", result.Error.Code);
        Assert.Empty(payment.Refunds);
    }

    [Fact]
    public void CancelByAdmin_on_a_legally_held_payment_is_rejected()
    {
        var payment = CreatePendingPayment();
        payment.SetLegalHold(true, Guid.Empty, DateTime.UtcNow);

        var result = payment.CancelByAdmin("duplicate charge", Guid.Empty, DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal("Payment.LegalHeld", result.Error.Code);
        Assert.Equal(PaymentStatus.Pending, payment.Status);
    }

    [Fact]
    public void CancelByAdmin_from_Pending_succeeds()
    {
        var payment = CreatePendingPayment();

        var result = payment.CancelByAdmin("duplicate charge", Guid.Empty, DateTime.UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentStatus.Cancelled, payment.Status);
    }

    [Fact]
    public void MarkChargedBack_from_Succeeded_succeeds()
    {
        var payment = CreateSucceededPayment();
        var nowUtc = DateTime.UtcNow;

        var result = payment.MarkChargedBack(nowUtc, "issuer dispute", Guid.Empty);

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentStatus.ChargedBack, payment.Status);
    }

    [Fact]
    public void PrepareForRetry_reopens_a_Failed_payment_with_a_due_retry()
    {
        var payment = CreatePendingPayment();
        var nowUtc = DateTime.UtcNow;
        payment.MarkFailed("card_declined", "declined", willRetry: true, nowUtc.AddHours(1), Guid.Empty, nowUtc);

        var result = payment.PrepareForRetry(Guid.Empty, nowUtc.AddHours(2));

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentStatus.Pending, payment.Status);
        Assert.Null(payment.NextRetryAtUtc);
        Assert.Null(payment.FailureCode);
    }

    [Fact]
    public void PrepareForRetry_before_the_scheduled_time_is_rejected()
    {
        var payment = CreatePendingPayment();
        var nowUtc = DateTime.UtcNow;
        payment.MarkFailed("card_declined", "declined", willRetry: true, nowUtc.AddHours(1), Guid.Empty, nowUtc);

        var result = payment.PrepareForRetry(Guid.Empty, nowUtc);

        Assert.True(result.IsFailure);
        Assert.Equal("SaaSPayment.RetryNotDue", result.Error.Code);
        Assert.Equal(PaymentStatus.Failed, payment.Status);
    }

    [Fact]
    public void PrepareForRetry_with_no_retry_scheduled_is_rejected()
    {
        var payment = CreatePendingPayment();
        payment.MarkFailed("card_declined", "declined", willRetry: false, nextRetryAtUtc: null, Guid.Empty, DateTime.UtcNow);

        var result = payment.PrepareForRetry(Guid.Empty, DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal("SaaSPayment.NoRetryScheduled", result.Error.Code);
    }

    [Fact]
    public void PrepareForRetry_from_a_non_Failed_status_is_rejected()
    {
        var payment = CreatePendingPayment();

        var result = payment.PrepareForRetry(Guid.Empty, DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal("SaaSPayment.InvalidTransition", result.Error.Code);
    }

    private static SaaSPayment CreatePendingPayment(string idempotencyKey = "key-1")
    {
        return SaaSPayment.Create(
            Guid.NewGuid(),
            IdempotencyKey.Create(idempotencyKey).Value,
            Money.Create(1999, "USD").Value,
            SaaSPaymentType.SubscriptionRenewal,
            Guid.NewGuid(),
            PaymentProviderCode.Stripe,
            StatementDescriptor.Create("TAXVISION SAAS").Value,
            Guid.Empty,
            DateTime.UtcNow).Value;
    }

    private static SaaSPayment CreateSucceededPayment()
    {
        var payment = CreatePendingPayment();
        var reference = ExternalPaymentReference.Create(PaymentProviderCode.Stripe, "pi_123").Value;
        var nowUtc = DateTime.UtcNow;

        payment.MarkProcessing(reference, "200", null, Guid.Empty, nowUtc);
        payment.MarkSucceeded(nowUtc, Guid.Empty);
        return payment;
    }
}
