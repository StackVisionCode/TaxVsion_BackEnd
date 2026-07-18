using TaxVision.PaymentClient.Domain.TenantPayments;
using TaxVision.PaymentClient.Domain.ValueObjects;

namespace TaxVision.PaymentClient.Tests.Domain;

public sealed class TenantPaymentTests
{
    [Fact]
    public void Create_with_a_zero_amount_fails()
    {
        var result = TenantPayment.Create(
            Guid.NewGuid(),
            IdempotencyKey.Create("key-1").Value,
            Money.Zero("USD"),
            TaxpayerId(),
            Purpose(),
            PaymentProviderCode.Stripe,
            StatementDescriptor.Create("ACME TAX SVC").Value,
            Guid.Empty,
            DateTime.UtcNow
        );

        Assert.True(result.IsFailure);
        Assert.Equal("TenantPayment.InvalidAmount", result.Error.Code);
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
    public void Create_allows_a_null_taxpayer_for_guest_checkout()
    {
        var payment = TenantPayment
            .Create(
                Guid.NewGuid(),
                IdempotencyKey.Create("key-1").Value,
                Money.Create(1999, "USD").Value,
                taxpayerId: null,
                Purpose(),
                PaymentProviderCode.Stripe,
                StatementDescriptor.Create("ACME TAX SVC").Value,
                Guid.Empty,
                DateTime.UtcNow
            )
            .Value;

        Assert.Null(payment.TaxpayerId);
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
    public void MarkFailed_without_retry_leaves_no_scheduled_retry()
    {
        var payment = CreatePendingPayment();

        var result = payment.MarkFailed(
            "card_declined",
            "The card was declined.",
            willRetry: false,
            nextRetryAtUtc: null,
            Guid.Empty,
            DateTime.UtcNow
        );

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
        Assert.Equal("TenantPayment.InvalidTransition", result.Error.Code);
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

        var result = payment.RefundPartial(
            Money.Create(2000, "USD").Value,
            "customer request",
            Guid.Empty,
            DateTime.UtcNow
        );

        Assert.True(result.IsFailure);
        Assert.Equal("TenantPayment.RefundExceedsPrincipal", result.Error.Code);
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
    public void PrepareForRetry_before_the_scheduled_time_is_rejected()
    {
        var payment = CreatePendingPayment();
        var nowUtc = DateTime.UtcNow;
        payment.MarkFailed("card_declined", "declined", willRetry: true, nowUtc.AddHours(1), Guid.Empty, nowUtc);

        var result = payment.PrepareForRetry(Guid.Empty, nowUtc);

        Assert.True(result.IsFailure);
        Assert.Equal("TenantPayment.RetryNotDue", result.Error.Code);
        Assert.Equal(PaymentStatus.Failed, payment.Status);
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

    private static PaymentPurpose Purpose() =>
        PaymentPurpose.Create(PaymentPurposeKind.InvoicePayment, "inv-001").Value;

    private static Guid TaxpayerId() => Guid.NewGuid();

    private static TenantPayment CreatePendingPayment(string idempotencyKey = "key-1")
    {
        return TenantPayment
            .Create(
                Guid.NewGuid(),
                IdempotencyKey.Create(idempotencyKey).Value,
                Money.Create(1999, "USD").Value,
                TaxpayerId(),
                Purpose(),
                PaymentProviderCode.Stripe,
                StatementDescriptor.Create("ACME TAX SVC").Value,
                Guid.Empty,
                DateTime.UtcNow
            )
            .Value;
    }

    private static TenantPayment CreateSucceededPayment()
    {
        var payment = CreatePendingPayment();
        var reference = ExternalPaymentReference.Create(PaymentProviderCode.Stripe, "pi_123").Value;
        var nowUtc = DateTime.UtcNow;

        payment.MarkProcessing(reference, "200", null, Guid.Empty, nowUtc);
        payment.MarkSucceeded(nowUtc, Guid.Empty);
        return payment;
    }
}
