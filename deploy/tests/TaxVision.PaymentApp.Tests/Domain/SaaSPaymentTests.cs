using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.SaaSPayments.Events;
using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Tests.Domain;

public sealed class SaaSPaymentTests
{
    [Fact]
    public void Create_with_a_zero_amount_fails()
    {
        var result = SaaSPayment.Create(
            Guid.NewGuid(),
            IdempotencyKey.Create("key-1").Value,
            Money.Zero("USD"),
            SaaSPaymentType.SubscriptionRenewal,
            Guid.NewGuid(),
            PaymentProviderCode.Stripe,
            StatementDescriptor.Create("TAXVISION SAAS").Value,
            Guid.Empty,
            DateTime.UtcNow
        );

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
    public void PrepareForOnboardingRetry_reopens_a_failed_onboarding_payment_and_clears_the_old_session()
    {
        var payment = FailedOnboardingPayment();

        var result = payment.PrepareForOnboardingRetry(DateTime.UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentStatus.Pending, payment.Status);
        Assert.Null(payment.ProviderCheckoutSessionId);
        Assert.Null(payment.ExternalChargeReference);
        Assert.Null(payment.FailureCode);
    }

    [Fact]
    public void PrepareForOnboardingRetry_rejects_a_non_onboarding_payment()
    {
        var payment = CreatePendingPayment();

        var result = payment.PrepareForOnboardingRetry(DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal("SaaSPayment.InvalidTransition", result.Error.Code);
    }

    [Fact]
    public void PrepareForOnboardingRetry_rejects_a_non_failed_onboarding_payment()
    {
        var payment = PendingOnboardingPayment();

        var result = payment.PrepareForOnboardingRetry(DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal("SaaSPayment.InvalidTransition", result.Error.Code);
    }

    [Fact]
    public void FlagForReview_KeepsStatus_AndIsIdempotent()
    {
        var payment = FailedOnboardingPayment();
        payment.MarkCancelled("BuyerCancelled", DateTime.UtcNow);
        var firstFlag = new DateTime(2026, 11, 5, 10, 0, 0, DateTimeKind.Utc);

        var first = payment.FlagForReview("LateProviderSuccess", "pi_123", 4900, "usd", firstFlag);
        var second = payment.FlagForReview("AmountMismatch", "pi_456", 100, "USD", firstFlag.AddMinutes(5));

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(PaymentStatus.Cancelled, payment.Status);
        Assert.Equal(firstFlag, payment.ReviewRequiredAtUtc);
        Assert.Equal("LateProviderSuccess", payment.ReviewReasonCode);
        Assert.Equal("pi_123", payment.ReviewProviderReference);
        Assert.Equal(4900, payment.ReviewPaidAmountCents);
        Assert.Equal("USD", payment.ReviewPaidCurrency);

        var admin = Guid.NewGuid();
        var resolved = payment.ResolveReview("Refunded", admin, firstFlag.AddHours(1));
        Assert.True(resolved.IsSuccess);
        Assert.Equal(firstFlag.AddHours(1), payment.ReviewResolvedAtUtc);
        Assert.Equal(admin, payment.ReviewResolvedByUserId);
        Assert.Equal("Refunded", payment.ReviewResolution);
        Assert.Equal(PaymentStatus.Cancelled, payment.Status);

        var again = payment.ResolveReview("NoActionNeeded", admin, firstFlag.AddHours(2));
        Assert.True(again.IsFailure);
        Assert.Equal("SaaSPayment.ReviewAlreadyResolved", again.Error.Code);
    }

    [Fact]
    public void FlagForReview_rejects_an_unknown_reason()
    {
        var payment = FailedOnboardingPayment();

        var result = payment.FlagForReview("Whatever", null, null, null, DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Null(payment.ReviewRequiredAtUtc);
    }

    [Fact]
    public void ResolveReview_requires_a_pending_review_and_a_known_resolution()
    {
        var payment = FailedOnboardingPayment();

        var notFlagged = payment.ResolveReview("Refunded", Guid.NewGuid(), DateTime.UtcNow);
        payment.FlagForReview("LateProviderSuccess", "pi_1", 4900, "USD", DateTime.UtcNow);
        var unknown = payment.ResolveReview("Ignored", Guid.NewGuid(), DateTime.UtcNow);

        Assert.Equal("SaaSPayment.ReviewNotRequired", notFlagged.Error.Code);
        Assert.Equal("SaaSPayment.InvalidReviewResolution", unknown.Error.Code);
        Assert.Null(payment.ReviewResolvedAtUtc);
    }

    private static SaaSPayment PendingOnboardingPayment() =>
        SaaSPayment
            .CreateForOnboarding(
                Guid.NewGuid(),
                IdempotencyKey.Create("onboarding-key-pending").Value,
                Money.Create(4900, "USD").Value,
                Guid.NewGuid(),
                PaymentProviderCode.Stripe,
                StatementDescriptor.Create("TAXVISION SAAS").Value,
                DateTime.UtcNow
            )
            .Value;

    private static SaaSPayment FailedOnboardingPayment()
    {
        var now = DateTime.UtcNow;
        var payment = PendingOnboardingPayment();
        payment.RecordHostedCheckoutSession(
            "sess_1",
            ExternalPaymentReference.Create(PaymentProviderCode.Stripe, "pi_1").Value,
            "https://checkout.example.com/1",
            now
        );
        payment.MarkFailed("card_declined", "declined", DunningDecision.NoRetry, Guid.Empty, now);
        return payment;
    }

    [Fact]
    public void MarkFailed_from_Pending_succeeds_and_schedules_a_retry()
    {
        var payment = CreatePendingPayment();
        var nowUtc = DateTime.UtcNow;
        var nextRetry = nowUtc.AddHours(1);

        var result = payment.MarkFailed(
            "card_declined",
            "The card was declined.",
            DunningDecision.RetryAt(nextRetry),
            Guid.Empty,
            nowUtc
        );

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

        var result = payment.MarkFailed(
            "card_declined",
            "The card was declined.",
            DunningDecision.NoRetry,
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

        var result = payment.MarkFailed(
            "card_declined",
            "reason",
            DunningDecision.NoRetry,
            Guid.Empty,
            DateTime.UtcNow
        );

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

        var result = payment.RefundPartial(
            Money.Create(2000, "USD").Value,
            "customer request",
            Guid.Empty,
            DateTime.UtcNow
        );

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
        payment.SetLegalHold(true, Guid.NewGuid(), DateTime.UtcNow);

        var result = payment.RefundPartial(Money.Create(100, "USD").Value, "reason", Guid.Empty, DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal("Payment.LegalHeld", result.Error.Code);
        Assert.Empty(payment.Refunds);
    }

    [Fact]
    public void CanRefund_rejects_legal_hold_without_mutating()
    {
        var payment = CreateSucceededPayment();
        payment.SetLegalHold(true, Guid.NewGuid(), DateTime.UtcNow);

        var result = payment.CanRefund(Money.Create(1000, "USD").Value, "customer request");

        Assert.True(result.IsFailure);
        Assert.Equal("Payment.LegalHeld", result.Error.Code);
        Assert.Empty(payment.Refunds);
        Assert.Equal(PaymentStatus.Succeeded, payment.Status);
    }

    [Fact]
    public void Refund_request_already_recorded_by_the_provider_sync_adds_no_second_line()
    {
        var payment = CreateSucceededPayment();
        var nowUtc = DateTime.UtcNow;
        var request = payment
            .OpenRefundRequest(
                Money.Create(1000, "USD").Value,
                "customer request",
                Guid.NewGuid(),
                "auto:1",
                null,
                nowUtc
            )
            .Value;
        // El webhook charge.refunded llegó antes de registrar el resultado.
        payment.ApplyProviderRefundTotal(1000, nowUtc);

        var completed = payment.CompleteRefundRequest(request.Id, "re_1", nowUtc);

        Assert.False(completed.Value);
        Assert.Single(payment.Refunds);
        Assert.Equal(RefundRequestStatus.Completed, request.Status);
    }

    [Fact]
    public void Second_refund_request_while_one_is_pending_is_rejected()
    {
        var payment = CreateSucceededPayment();
        var nowUtc = DateTime.UtcNow;
        payment.OpenRefundRequest(Money.Create(1000, "USD").Value, "first", Guid.NewGuid(), "auto:1", null, nowUtc);

        var second = payment.OpenRefundRequest(
            Money.Create(500, "USD").Value,
            "second",
            Guid.NewGuid(),
            "auto:2",
            null,
            nowUtc
        );

        Assert.Equal("SaaSPayment.RefundInProgress", second.Error.Code);
        Assert.Single(payment.RefundRequests);
    }

    [Fact]
    public void CancelByAdmin_on_a_legally_held_payment_is_rejected()
    {
        var payment = CreatePendingPayment();
        payment.SetLegalHold(true, Guid.NewGuid(), DateTime.UtcNow);

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
        payment.MarkFailed(
            "card_declined",
            "declined",
            DunningDecision.RetryAt(nowUtc.AddHours(1)),
            Guid.Empty,
            nowUtc
        );

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
        payment.MarkFailed(
            "card_declined",
            "declined",
            DunningDecision.RetryAt(nowUtc.AddHours(1)),
            Guid.Empty,
            nowUtc
        );

        var result = payment.PrepareForRetry(Guid.Empty, nowUtc);

        Assert.True(result.IsFailure);
        Assert.Equal("SaaSPayment.RetryNotDue", result.Error.Code);
        Assert.Equal(PaymentStatus.Failed, payment.Status);
    }

    [Fact]
    public void PrepareForRetry_with_no_retry_scheduled_is_rejected()
    {
        var payment = CreatePendingPayment();
        payment.MarkFailed("card_declined", "declined", DunningDecision.NoRetry, Guid.Empty, DateTime.UtcNow);

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

    // ---------- PayFlow (Fase 8) — CreateForOnboarding / RecordHostedCheckoutSession ----------

    [Fact]
    public void CreateForOnboarding_starts_Pending_with_TenantId_Empty_and_OnboardingId_set()
    {
        var onboardingId = Guid.NewGuid();

        var payment = CreateOnboardingPayment(onboardingId);

        Assert.Equal(PaymentStatus.Pending, payment.Status);
        Assert.Equal(SaaSPaymentType.OnboardingInitial, payment.Type);
        Assert.Equal(Guid.Empty, payment.TenantId);
        Assert.Equal(onboardingId, payment.OnboardingId);
        Assert.Equal(Guid.Empty, payment.CreatedBy);
    }

    [Fact]
    public void CreateForOnboarding_rejects_empty_onboarding_id()
    {
        var result = SaaSPayment.CreateForOnboarding(
            Guid.Empty,
            IdempotencyKey.Create("onboarding-1").Value,
            Money.Create(9900, "USD").Value,
            Guid.NewGuid(),
            PaymentProviderCode.Stripe,
            StatementDescriptor.Create("TAXVISION SAAS").Value,
            DateTime.UtcNow
        );

        Assert.True(result.IsFailure);
        Assert.Equal("SaaSPayment.InvalidOnboarding", result.Error.Code);
    }

    [Fact]
    public void CreateForOnboarding_rejects_zero_amount()
    {
        var result = SaaSPayment.CreateForOnboarding(
            Guid.NewGuid(),
            IdempotencyKey.Create("onboarding-1").Value,
            Money.Zero("USD"),
            Guid.NewGuid(),
            PaymentProviderCode.Stripe,
            StatementDescriptor.Create("TAXVISION SAAS").Value,
            DateTime.UtcNow
        );

        Assert.True(result.IsFailure);
        Assert.Equal("SaaSPayment.InvalidAmount", result.Error.Code);
    }

    [Fact]
    public void RecordHostedCheckoutSession_transitions_Pending_to_Processing_and_records_an_attempt()
    {
        var payment = CreateOnboardingPayment(Guid.NewGuid());
        var reference = ExternalPaymentReference.Create(PaymentProviderCode.Stripe, "pi_onboarding_1").Value;
        var nowUtc = DateTime.UtcNow;

        var result = payment.RecordHostedCheckoutSession(
            "cs_test_123",
            reference,
            "https://checkout.stripe.com/cs_test_123",
            nowUtc
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentStatus.Processing, payment.Status);
        Assert.Equal("cs_test_123", payment.ProviderCheckoutSessionId);
        Assert.Equal(reference, payment.ExternalChargeReference);
        Assert.Equal("https://checkout.stripe.com/cs_test_123", payment.NextActionUrl);
        Assert.Single(payment.Attempts);
    }

    [Fact]
    public void RecordHostedCheckoutSession_is_idempotent_for_the_same_session_id()
    {
        var payment = CreateOnboardingPayment(Guid.NewGuid());
        var reference = ExternalPaymentReference.Create(PaymentProviderCode.Stripe, "pi_onboarding_1").Value;
        var nowUtc = DateTime.UtcNow;
        payment.RecordHostedCheckoutSession(
            "cs_test_123",
            reference,
            "https://checkout.stripe.com/cs_test_123",
            nowUtc
        );

        var result = payment.RecordHostedCheckoutSession(
            "cs_test_123",
            reference,
            "https://checkout.stripe.com/cs_test_123",
            nowUtc.AddSeconds(1)
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentStatus.Processing, payment.Status);
        Assert.Single(payment.Attempts);
    }

    [Fact]
    public void RecordHostedCheckoutSession_rejects_wrong_state()
    {
        var payment = CreateOnboardingPayment(Guid.NewGuid());
        var reference = ExternalPaymentReference.Create(PaymentProviderCode.Stripe, "pi_onboarding_1").Value;
        var nowUtc = DateTime.UtcNow;
        payment.RecordHostedCheckoutSession(
            "cs_test_123",
            reference,
            "https://checkout.stripe.com/cs_test_123",
            nowUtc
        );

        var result = payment.RecordHostedCheckoutSession(
            "cs_test_456",
            reference,
            "https://checkout.stripe.com/cs_test_456",
            nowUtc
        );

        Assert.True(result.IsFailure);
        Assert.Equal("SaaSPayment.InvalidTransition", result.Error.Code);
    }

    // PAY-BB1: cada transición de resultado registra su evento de dominio una sola vez.
    [Fact]
    public void MarkSucceeded_raises_SaaSPaymentSucceeded_once()
    {
        var payment = CreatePendingPayment();
        var nowUtc = DateTime.UtcNow;
        payment.MarkProcessing(
            ExternalPaymentReference.Create(PaymentProviderCode.Stripe, "pi_123").Value,
            "200",
            null,
            Guid.Empty,
            nowUtc
        );
        Assert.Empty(payment.DomainEvents);

        Assert.True(payment.MarkSucceeded(nowUtc, Guid.Empty).IsSuccess);
        Assert.True(payment.MarkSucceeded(nowUtc, Guid.Empty).IsFailure);

        var raised = Assert.IsType<SaaSPaymentSucceeded>(Assert.Single(payment.DomainEvents));
        Assert.Equal(payment.Id, raised.Payment.SaaSPaymentId);
        Assert.Equal(PaymentStatus.Succeeded, raised.Payment.Status);
        Assert.Equal("pi_123", raised.Payment.ExternalChargeReference);
        Assert.Equal(nowUtc, raised.Payment.PaidAtUtc);
    }

    [Fact]
    public void A_refund_raises_SaaSPaymentRefunded_with_the_delta_and_the_cumulative_total()
    {
        var payment = CreateSucceededPayment();
        payment.ClearDomainEvents();
        var nowUtc = DateTime.UtcNow;

        Assert.True(payment.RefundPartial(Money.Create(999, "USD").Value, "customer request", Guid.Empty, nowUtc).IsSuccess);
        Assert.True(payment.RefundPartial(Money.Create(1000, "USD").Value, "customer request", Guid.Empty, nowUtc).IsSuccess);

        var events = payment.DomainEvents.OfType<SaaSPaymentRefunded>().ToList();
        Assert.Equal(2, events.Count);
        Assert.Equal(999, events[0].RefundDeltaCents);
        Assert.Equal(999, events[0].CumulativeRefundedCents);
        Assert.Equal(1000, events[1].RefundDeltaCents);
        Assert.Equal(1999, events[1].CumulativeRefundedCents);
        Assert.Equal(payment.Id, events[1].Payment.SaaSPaymentId);
    }

    [Fact]
    public void A_rejected_refund_raises_nothing()
    {
        var payment = CreatePendingPayment();

        Assert.True(payment.RefundPartial(Money.Create(100, "USD").Value, "x", Guid.Empty, DateTime.UtcNow).IsFailure);

        Assert.Empty(payment.DomainEvents);
    }

    [Fact]
    public void A_chargeback_raises_SaaSPaymentChargedBack()
    {
        var payment = CreateSucceededPayment();
        payment.ClearDomainEvents();
        var nowUtc = DateTime.UtcNow;

        Assert.True(payment.MarkChargedBack(nowUtc, "issuer dispute", Guid.Empty).IsSuccess);

        var raised = Assert.IsType<SaaSPaymentChargedBack>(Assert.Single(payment.DomainEvents));
        Assert.Equal(payment.Id, raised.Payment.SaaSPaymentId);
        Assert.Equal(nowUtc, raised.ChargedBackAtUtc);
    }

    [Fact]
    public void MarkFailed_raises_SaaSPaymentFailed_with_its_retry_and_attempt()
    {
        var payment = CreatePendingPayment();
        var retryAtUtc = DateTime.UtcNow.AddHours(1);

        Assert.True(
            payment
                .MarkFailed(
                    "card_declined",
                    "Declined.",
                    DunningDecision.RetryAt(retryAtUtc),
                    Guid.Empty,
                    DateTime.UtcNow
                )
                .IsSuccess
        );

        var raised = Assert.IsType<SaaSPaymentFailed>(Assert.Single(payment.DomainEvents));
        Assert.Equal("card_declined", raised.Payment.FailureCode);
        Assert.Equal(retryAtUtc, raised.Payment.NextRetryAtUtc);
        Assert.Equal(1, raised.Payment.AttemptCount);
    }

    [Fact]
    public void CancelByAdmin_raises_an_announced_cancellation()
    {
        var payment = CreatePendingPayment();

        Assert.True(payment.CancelByAdmin("ProviderCancelled", Guid.Empty, DateTime.UtcNow).IsSuccess);

        var raised = Assert.IsType<SaaSPaymentCancelled>(Assert.Single(payment.DomainEvents));
        Assert.False(raised.PropagatedFromOwner);
        Assert.Equal(PaymentStatus.Cancelled, raised.Payment.Status);
    }

    // La cancelación que pide el dueño del cobro no se le vuelve a anunciar; una redelivery no agrega eventos.
    [Fact]
    public void MarkCancelled_raises_a_propagated_cancellation_once()
    {
        var payment = CreatePendingPayment();

        Assert.True(payment.MarkCancelled("Superseded.", DateTime.UtcNow).IsSuccess);
        Assert.True(payment.MarkCancelled("Superseded.", DateTime.UtcNow).IsSuccess);

        var raised = Assert.IsType<SaaSPaymentCancelled>(Assert.Single(payment.DomainEvents));
        Assert.True(raised.PropagatedFromOwner);
    }

    [Fact]
    public void FlagForReview_raises_SaaSPaymentReviewRequired_once()
    {
        var payment = CreatePendingPayment();
        payment.MarkFailed("card_declined", "Declined.", DunningDecision.NoRetry, Guid.Empty, DateTime.UtcNow);
        payment.ClearDomainEvents();

        payment.FlagForReview(SaaSPayment.ReviewReasonLateProviderSuccess, "pi_late", 1999, "usd", DateTime.UtcNow);
        payment.FlagForReview(SaaSPayment.ReviewReasonLateProviderSuccess, "pi_late", 1999, "usd", DateTime.UtcNow);

        var raised = Assert.IsType<SaaSPaymentReviewRequired>(Assert.Single(payment.DomainEvents));
        Assert.Equal(SaaSPayment.ReviewReasonLateProviderSuccess, raised.ReasonCode);
        Assert.Equal("pi_late", raised.ProviderReference);
    }

    private static SaaSPayment CreateOnboardingPayment(Guid onboardingId, string idempotencyKey = "onboarding-1") =>
        SaaSPayment
            .CreateForOnboarding(
                onboardingId,
                IdempotencyKey.Create(idempotencyKey).Value,
                Money.Create(9900, "USD").Value,
                Guid.NewGuid(),
                PaymentProviderCode.Stripe,
                StatementDescriptor.Create("TAXVISION SAAS").Value,
                DateTime.UtcNow
            )
            .Value;

    [Fact]
    public void AttachConcept_after_charge_is_rejected()
    {
        var payment = CreateSucceededPayment();

        var result = payment.AttachConcept(ChargeConcept.Create("add-on-purchase", "Add-on").Value);

        Assert.Equal("SaaSPayment.ConceptAlreadySet", result.Error.Code);
        Assert.Null(payment.Concept);
    }

    [Fact]
    public void AttachConcept_twice_keeps_the_first_concept()
    {
        var payment = CreatePendingPayment();
        var first = ChargeConcept.Create("subscription-renewal", "Subscription renewal").Value;
        payment.AttachConcept(first);

        var result = payment.AttachConcept(ChargeConcept.Create("add-on-purchase", "Add-on").Value);

        Assert.True(result.IsFailure);
        Assert.Equal(first, payment.Concept);
    }

    [Fact]
    public void SetLegalHold_same_value_is_noop_and_requires_actor()
    {
        var payment = CreatePendingPayment();
        var updatedAt = payment.UpdatedAtUtc;
        var later = updatedAt.AddHours(1);

        var same = payment.SetLegalHold(false, Guid.NewGuid(), later);
        var noActor = payment.SetLegalHold(true, Guid.Empty, later);

        Assert.True(same.IsSuccess);
        Assert.Equal(updatedAt, payment.UpdatedAtUtc);
        Assert.Equal(Guid.Empty, payment.UpdatedBy);
        Assert.Equal("SaaSPayment.ActorRequired", noActor.Error.Code);
        Assert.False(payment.IsLegalHeld);
    }

    private static SaaSPayment CreatePendingPayment(string idempotencyKey = "key-1")
    {
        return SaaSPayment
            .Create(
                Guid.NewGuid(),
                IdempotencyKey.Create(idempotencyKey).Value,
                Money.Create(1999, "USD").Value,
                SaaSPaymentType.SubscriptionRenewal,
                Guid.NewGuid(),
                PaymentProviderCode.Stripe,
                StatementDescriptor.Create("TAXVISION SAAS").Value,
                Guid.Empty,
                DateTime.UtcNow
            )
            .Value;
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
