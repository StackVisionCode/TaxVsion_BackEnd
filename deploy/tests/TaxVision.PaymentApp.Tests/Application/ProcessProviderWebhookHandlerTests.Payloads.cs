using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Application.SaaSPayments.Commands.ProcessProviderWebhook;
using TaxVision.PaymentApp.Domain.Audit;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.ValueObjects;
using TaxVision.PaymentApp.Domain.Webhooks;

namespace TaxVision.PaymentApp.Tests.Application;

/// <summary>Caracterización de PAY-7: lo que hoy hace cada estado del payload, antes de partir el handler.</summary>
public sealed partial class ProcessProviderWebhookHandlerTests
{
    private const string OffSessionReference = "pi_offsession_1";

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 6)]
    [InlineData(3, 24)]
    public async Task Failed_payload_follows_the_dunning_schedule(int attempts, int hours)
    {
        var payment = PaymentWithAttempts(SaaSPaymentType.SubscriptionRenewal, attempts);
        var before = DateTime.UtcNow;

        await HandleStripeAsync(payment, Payload(PaymentStatus.Failed, code: "card_declined", message: "No."));

        var after = DateTime.UtcNow;
        Assert.Equal(PaymentStatus.Failed, payment.Status);
        Assert.Equal(attempts, payment.Attempts.Count);
        Assert.InRange(payment.NextRetryAtUtc!.Value, before.AddHours(hours), after.AddHours(hours));
        Assert.Equal("card_declined", payment.FailureCode);
        Assert.Equal("No.", payment.FailureReason);
    }

    [Fact]
    public async Task Failed_payload_on_the_last_attempt_gives_up()
    {
        var payment = PaymentWithAttempts(SaaSPaymentType.SubscriptionRenewal, 4);

        await HandleStripeAsync(payment, Payload(PaymentStatus.Failed));

        Assert.Equal(PaymentStatus.Failed, payment.Status);
        Assert.Null(payment.NextRetryAtUtc);
    }

    [Fact]
    public async Task Failed_payload_without_dunning_does_not_retry_and_uses_the_default_texts()
    {
        var payment = CreateProcessingOffSessionPayment(SaaSPaymentType.PlanChangeCharge);

        await HandleStripeAsync(payment, Payload(PaymentStatus.Failed));

        Assert.Null(payment.NextRetryAtUtc);
        Assert.Equal("Provider.Unknown", payment.FailureCode);
        Assert.Equal("The provider declined the charge.", payment.FailureReason);
    }

    // El webhook no mira si el código es permanente: a diferencia del cobro síncrono, agenda reintento.
    [Fact]
    public async Task Failed_payload_with_a_permanent_code_still_schedules_a_retry()
    {
        var payment = CreateProcessingOffSessionPayment(SaaSPaymentType.SubscriptionRenewal);

        await HandleStripeAsync(payment, Payload(PaymentStatus.Failed, code: "requires_payment_method"));

        Assert.NotNull(payment.NextRetryAtUtc);
    }

    [Fact]
    public async Task Processing_payload_is_applied_without_moving_the_payment()
    {
        var payment = CreateProcessingOffSessionPayment(SaaSPaymentType.SubscriptionRenewal);
        var webhooks = new FakeWebhookEventRepository();
        var audit = new FakePaymentAuditLogWriter();

        var result = await HandleStripeAsync(payment, Payload(PaymentStatus.Processing), webhooks, audit: audit);

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentStatus.Processing, payment.Status);
        Assert.Equal(WebhookEventStatus.Applied, webhooks.Added!.Status);
        Assert.Equal(payment.Id, webhooks.Added.RelatedSaaSPaymentId);
        Assert.Equal(PaymentAuditAction.SaaSPaymentMarkedProcessing, Assert.Single(audit.Entries).Action);
    }

    [Fact]
    public async Task Cancelled_payload_cancels_the_payment()
    {
        var payment = CreateProcessingOffSessionPayment(SaaSPaymentType.SubscriptionRenewal);
        var audit = new FakePaymentAuditLogWriter();

        await HandleStripeAsync(payment, Payload(PaymentStatus.Cancelled), audit: audit);

        Assert.Equal(PaymentStatus.Cancelled, payment.Status);
        Assert.Equal("ProviderCancelled", payment.FailureReason);
        Assert.Equal(PaymentAuditAction.SaaSPaymentCancelled, Assert.Single(audit.Entries).Action);
    }

    [Fact]
    public async Task Cancelled_payload_on_a_legal_hold_is_stale()
    {
        var payment = CreateProcessingOffSessionPayment(SaaSPaymentType.SubscriptionRenewal);
        payment.SetLegalHold(true, Guid.NewGuid(), DateTime.UtcNow);
        var webhooks = new FakeWebhookEventRepository();

        var result = await HandleStripeAsync(payment, Payload(PaymentStatus.Cancelled), webhooks);

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentStatus.Processing, payment.Status);
        Assert.Equal(WebhookEventStatus.Stale, webhooks.Added!.Status);
    }

    [Fact]
    public async Task Refund_payload_applies_the_difference_with_what_is_already_tracked()
    {
        var payment = SucceededPayment();
        var metrics = new FakePaymentAppMetrics();
        var audit = new FakePaymentAuditLogWriter();

        await HandleStripeAsync(payment, Payload(PaymentStatus.Refunded, refunded: 500), metrics: metrics, audit: audit);
        await HandleStripeAsync(payment, Payload(PaymentStatus.Refunded, refunded: 800), metrics: metrics, audit: audit);

        Assert.Equal(PaymentStatus.PartiallyRefunded, payment.Status);
        Assert.Equal([500L, 300L], payment.Refunds.Select(r => r.Amount.AmountCents));
        Assert.All(payment.Refunds, r => Assert.Equal("USD", r.Amount.Currency));
        Assert.Equal(800, payment.RefundedAmountCents);
        Assert.Equal(2, metrics.Refunded);
        Assert.All(audit.Entries, e => Assert.Equal(PaymentAuditAction.SaaSPaymentRefundedPartial, e.Action));
    }

    [Fact]
    public async Task Refund_payload_for_the_whole_amount_closes_the_payment()
    {
        var payment = SucceededPayment();

        await HandleStripeAsync(payment, Payload(PaymentStatus.Refunded, refunded: payment.Amount.AmountCents));

        Assert.Equal(PaymentStatus.Refunded, payment.Status);
        Assert.Equal(payment.Amount.AmountCents, payment.RefundedAmountCents);
    }

    [Fact]
    public async Task Refund_payload_with_nothing_new_is_applied_without_a_new_line()
    {
        var payment = SucceededPayment();
        var metrics = new FakePaymentAppMetrics();
        await HandleStripeAsync(payment, Payload(PaymentStatus.Refunded, refunded: 500), metrics: metrics);
        var webhooks = new FakeWebhookEventRepository();

        await HandleStripeAsync(payment, Payload(PaymentStatus.Refunded, refunded: 500), webhooks, metrics);

        Assert.Single(payment.Refunds);
        Assert.Equal(1, metrics.Refunded);
        Assert.Equal(WebhookEventStatus.Applied, webhooks.Added!.Status);
    }

    [Fact]
    public async Task Refund_payload_over_the_amount_is_stale()
    {
        var payment = SucceededPayment();
        var webhooks = new FakeWebhookEventRepository();
        var metrics = new FakePaymentAppMetrics();

        await HandleStripeAsync(
            payment,
            Payload(PaymentStatus.Refunded, refunded: payment.Amount.AmountCents + 1),
            webhooks,
            metrics
        );

        Assert.Empty(payment.Refunds);
        Assert.Equal(PaymentStatus.Succeeded, payment.Status);
        Assert.Equal(WebhookEventStatus.Stale, webhooks.Added!.Status);
        Assert.Equal(0, metrics.Refunded);
    }

    [Fact]
    public async Task Refund_payload_without_an_amount_is_stale()
    {
        var payment = SucceededPayment();
        var webhooks = new FakeWebhookEventRepository();

        await HandleStripeAsync(payment, Payload(PaymentStatus.Refunded), webhooks);

        Assert.Empty(payment.Refunds);
        Assert.Equal(WebhookEventStatus.Stale, webhooks.Added!.Status);
        Assert.Contains("UnsupportedPaymentStatus", webhooks.Added.ProcessingError);
    }

    [Fact]
    public async Task ChargedBack_payload_marks_the_payment_and_counts_it()
    {
        var payment = SucceededPayment();
        var metrics = new FakePaymentAppMetrics();
        var audit = new FakePaymentAuditLogWriter();

        await HandleStripeAsync(payment, Payload(PaymentStatus.ChargedBack), metrics: metrics, audit: audit);

        Assert.Equal(PaymentStatus.ChargedBack, payment.Status);
        Assert.Equal("Chargeback dispute created.", payment.FailureReason);
        Assert.Equal(1, metrics.ChargedBack);
        Assert.Equal(PaymentAuditAction.SaaSPaymentChargedBack, Assert.Single(audit.Entries).Action);
    }

    [Fact]
    public async Task ChargedBack_payload_before_the_payment_is_paid_is_stale()
    {
        var payment = CreateProcessingOffSessionPayment(SaaSPaymentType.SubscriptionRenewal);
        var webhooks = new FakeWebhookEventRepository();
        var metrics = new FakePaymentAppMetrics();

        await HandleStripeAsync(payment, Payload(PaymentStatus.ChargedBack), webhooks, metrics);

        Assert.Equal(PaymentStatus.Processing, payment.Status);
        Assert.Equal(WebhookEventStatus.Stale, webhooks.Added!.Status);
        Assert.Equal(0, metrics.ChargedBack);
    }

    [Fact]
    public async Task RequiresAction_payload_is_not_actionable()
    {
        var payment = CreateProcessingOffSessionPayment(SaaSPaymentType.SubscriptionRenewal);
        var webhooks = new FakeWebhookEventRepository();

        var result = await HandleStripeAsync(payment, Payload(PaymentStatus.RequiresAction), webhooks);

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentStatus.Processing, payment.Status);
        Assert.Equal(WebhookEventStatus.Stale, webhooks.Added!.Status);
    }

    [Fact]
    public async Task Success_payload_with_another_currency_is_flagged_for_review()
    {
        var payment = CreateProcessingOffSessionPayment(SaaSPaymentType.SubscriptionRenewal);
        var webhooks = new FakeWebhookEventRepository();
        var audit = new FakePaymentAuditLogWriter();

        await HandleStripeAsync(
            payment,
            new WebhookEventPayload(OffSessionReference, PaymentStatus.Succeeded, null, null, null, null, 1500, "eur"),
            webhooks,
            audit: audit
        );

        Assert.Equal(PaymentStatus.Processing, payment.Status);
        Assert.Equal(SaaSPayment.ReviewReasonAmountMismatch, payment.ReviewReasonCode);
        Assert.Equal(1500, payment.ReviewPaidAmountCents);
        Assert.Equal("EUR", payment.ReviewPaidCurrency);
        Assert.Equal(WebhookEventStatus.NeedsReview, webhooks.Added!.Status);
        Assert.Equal(PaymentAuditAction.SaaSPaymentReviewRequired, Assert.Single(audit.Entries).Action);
    }

    [Fact]
    public async Task Success_payload_with_the_same_amount_and_no_currency_is_applied()
    {
        var payment = CreateProcessingOffSessionPayment(SaaSPaymentType.SubscriptionRenewal);

        await HandleStripeAsync(
            payment,
            new WebhookEventPayload(OffSessionReference, PaymentStatus.Succeeded, null, null, null, null, 1500, null)
        );

        Assert.Equal(PaymentStatus.Succeeded, payment.Status);
        Assert.NotNull(payment.PaidAtUtc);
    }

    [Fact]
    public async Task Second_review_keeps_the_first_mark_and_audits_once()
    {
        var payment = CreateProcessingOffSessionPayment(SaaSPaymentType.SubscriptionRenewal);
        var audit = new FakePaymentAuditLogWriter();
        var metrics = new FakePaymentAppMetrics();
        var mismatch = new WebhookEventPayload(
            OffSessionReference,
            PaymentStatus.Succeeded,
            null,
            null,
            null,
            null,
            999,
            "usd"
        );
        await HandleStripeAsync(payment, mismatch, metrics: metrics, audit: audit);
        var webhooks = new FakeWebhookEventRepository();

        await HandleStripeAsync(payment, mismatch with { PaidAmountCents = 111 }, webhooks, metrics, audit);

        Assert.Equal(999, payment.ReviewPaidAmountCents);
        Assert.Single(audit.Entries);
        Assert.Single(metrics.ReviewRequired);
        Assert.Equal(WebhookEventStatus.NeedsReview, webhooks.Added!.Status);
    }

    [Fact]
    public async Task Reconciled_reference_replaces_the_session_reference_before_applying()
    {
        var payment = CreateHostedCheckoutPayment("cs_ref_1");

        await HandleStripeAsync(
            payment,
            new WebhookEventPayload("cs_ref_1", PaymentStatus.Succeeded, null, null, null, ReconciledChargeReference: "pi_ref_1")
        );

        Assert.Equal(PaymentStatus.Succeeded, payment.Status);
        Assert.Equal("pi_ref_1", payment.ExternalChargeReference!.Value);
    }

    [Fact]
    public async Task Invalid_reconciled_reference_keeps_the_reference_and_still_applies()
    {
        var payment = CreateHostedCheckoutPayment("cs_ref_2");

        await HandleStripeAsync(
            payment,
            new WebhookEventPayload(
                "cs_ref_2",
                PaymentStatus.Succeeded,
                null,
                null,
                null,
                ReconciledChargeReference: new string('x', 201)
            )
        );

        Assert.Equal(PaymentStatus.Succeeded, payment.Status);
        Assert.Equal("cs_ref_2", payment.ExternalChargeReference!.Value);
    }

    [Fact]
    public async Task Unknown_charge_reference_is_rejected_with_a_success()
    {
        var payment = CreateProcessingOffSessionPayment(SaaSPaymentType.SubscriptionRenewal);
        var webhooks = new FakeWebhookEventRepository();

        var result = await HandleStripeAsync(
            payment,
            new WebhookEventPayload("pi_nobody", PaymentStatus.Succeeded, null, null, null),
            webhooks
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentStatus.Processing, payment.Status);
        Assert.Equal(WebhookEventStatus.Rejected, webhooks.Added!.Status);
    }

    [Fact]
    public async Task Unsupported_event_type_is_rejected_with_a_success()
    {
        var webhooks = new FakeWebhookEventRepository();

        var result = await RunStripeAsync(
            parseError: new Error("Stripe.Webhook.UnsupportedEventType", "Not handled."),
            webhooks: webhooks
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(WebhookEventStatus.Rejected, webhooks.Added!.Status);
    }

    [Fact]
    public async Task Unreadable_payload_fails_and_leaves_the_event_reprocessable()
    {
        var webhooks = new FakeWebhookEventRepository();

        var result = await RunStripeAsync(parseError: new Error("Stripe.Webhook.Invalid", "Bad."), webhooks: webhooks);

        Assert.Equal("Stripe.Webhook.Invalid", result.Error.Code);
        Assert.Equal(WebhookEventStatus.Failed, webhooks.Added!.Status);
        Assert.False(webhooks.Added.IsTerminal);
    }

    [Fact]
    public async Task Invalid_signature_stores_nothing_and_counts_the_failure()
    {
        var webhooks = new FakeWebhookEventRepository();
        var metrics = new FakePaymentAppMetrics();

        var result = await RunStripeAsync(
            verificationError: new Error("Stripe.Webhook.InvalidSignature", "Bad."),
            webhooks: webhooks,
            metrics: metrics
        );

        Assert.Equal("Stripe.Webhook.InvalidSignature", result.Error.Code);
        Assert.Null(webhooks.Added);
        Assert.Equal(1, metrics.Received);
        Assert.Equal(1, metrics.SignatureFailures);
    }

    [Fact]
    public async Task Provider_without_adapter_is_not_configured()
    {
        var provider = new FakePaymentProvider(
            PaymentProviderCode.PayPal,
            new WebhookVerificationResult("evt", "type", "{}"),
            Payload(PaymentStatus.Processing)
        );

        var result = await ProcessProviderWebhookHandler.Handle(
            new ProcessProviderWebhookCommand(PaymentProviderCode.Stripe, "{}", new Dictionary<string, string>()),
            new FakePaymentAdapterFactory(provider),
            new FakeProviderWebhookSecrets(),
            new FakeWebhookEventRepository(),
            new FakeSaaSPaymentRepository(CreateProcessingOffSessionPayment(SaaSPaymentType.SubscriptionRenewal)),
            new FakePaymentAuditLogWriter(),
            new FakeUnitOfWork(),
            new FakePaymentAppMetrics(),
            new FakePaymentAttemptThrottle(),
            new FakeCorrelationContext(),
            NullLogger<WebhookEvent>.Instance,
            CancellationToken.None
        );

        Assert.Equal("PaymentProvider.NotConfigured", result.Error.Code);
    }

    [Fact]
    public async Task Stripe_event_keeps_the_signature_header_case_insensitive()
    {
        var webhooks = new FakeWebhookEventRepository();

        await RunStripeAsync(
            headers: new Dictionary<string, string> { ["stripe-signature"] = "t=9,v1=abc" },
            webhooks: webhooks
        );

        Assert.Equal("t=9,v1=abc", webhooks.Added!.SignatureHeader);
    }

    [Fact]
    public async Task PayPal_event_keeps_the_five_signature_headers()
    {
        var payment = CreateProcessingOnboardingPayment();
        var webhooks = new FakeWebhookEventRepository();
        var provider = new FakePaymentProvider(
            PaymentProviderCode.PayPal,
            new WebhookVerificationResult("paypal-sig", "PAYMENT.CAPTURE.COMPLETED", "{}"),
            new WebhookEventPayload("ORDER-123", PaymentStatus.Processing, null, null, null)
        );

        await ProcessProviderWebhookHandler.Handle(
            new ProcessProviderWebhookCommand(PaymentProviderCode.PayPal, "{}", PayPalHeaders()),
            new FakePaymentAdapterFactory(provider),
            new FakeProviderWebhookSecrets(),
            webhooks,
            new FakeSaaSPaymentRepository(payment),
            new FakePaymentAuditLogWriter(),
            new FakeUnitOfWork(),
            new FakePaymentAppMetrics(),
            new FakePaymentAttemptThrottle(),
            new FakeCorrelationContext(),
            NullLogger<WebhookEvent>.Instance,
            CancellationToken.None
        );

        Assert.Equal(
            "PAYPAL-AUTH-ALGO=SHA256withRSA;PAYPAL-CERT-URL=https://api-m.sandbox.paypal.com/certs/test;"
                + "PAYPAL-TRANSMISSION-ID=transmission-id;PAYPAL-TRANSMISSION-SIG=signature;"
                + "PAYPAL-TRANSMISSION-TIME=2026-09-04T12:00:00Z",
            webhooks.Added!.SignatureHeader
        );
    }

    [Fact]
    public async Task Tenant_payments_are_throttled_per_tenant()
    {
        var payment = CreateProcessingOffSessionPayment(SaaSPaymentType.SubscriptionRenewal);
        var throttle = new FakePaymentAttemptThrottle();

        await ProcessProviderWebhookHandler.Handle(
            new ProcessProviderWebhookCommand(
                PaymentProviderCode.Stripe,
                "{}",
                new Dictionary<string, string> { ["Stripe-Signature"] = "t=1,v1=test" }
            ),
            new FakePaymentAdapterFactory(
                new FakePaymentProvider(
                    PaymentProviderCode.Stripe,
                    new WebhookVerificationResult("evt_scope", "payment_intent.updated", "{}"),
                    Payload(PaymentStatus.Processing)
                )
            ),
            new FakeProviderWebhookSecrets(),
            new FakeWebhookEventRepository(),
            new FakeSaaSPaymentRepository(payment),
            new FakePaymentAuditLogWriter(),
            new FakeUnitOfWork(),
            new FakePaymentAppMetrics(),
            throttle,
            new FakeCorrelationContext(),
            NullLogger<WebhookEvent>.Instance,
            CancellationToken.None
        );

        Assert.Equal(payment.TenantId, Assert.Single(throttle.CheckedScopes));
    }

    private static WebhookEventPayload Payload(
        PaymentStatus status,
        string? code = null,
        string? message = null,
        long? refunded = null
    ) => new(OffSessionReference, status, code, message, refunded);

    private static SaaSPayment SucceededPayment()
    {
        var payment = CreateProcessingOffSessionPayment(SaaSPaymentType.SubscriptionRenewal);
        Assert.True(payment.MarkSucceeded(DateTime.UtcNow, Guid.Empty).IsSuccess);
        return payment;
    }

    /// <summary>Pago en Processing cuyo último intento es el número <paramref name="attempts"/>.</summary>
    private static SaaSPayment PaymentWithAttempts(SaaSPaymentType type, int attempts)
    {
        var payment = CreateProcessingOffSessionPayment(type);
        var nowUtc = DateTime.UtcNow;
        for (var attempt = 1; attempt < attempts; attempt++)
        {
            Assert.True(payment.MarkFailed("card_declined", "No.", DunningDecision.RetryAt(nowUtc), Guid.Empty, nowUtc).IsSuccess);
            Assert.True(payment.PrepareForRetry(Guid.Empty, nowUtc).IsSuccess);
            Assert.True(
                payment
                    .MarkProcessing(
                        ExternalPaymentReference.Create(PaymentProviderCode.Stripe, OffSessionReference).Value,
                        "processing",
                        providerResponseBody: null,
                        Guid.Empty,
                        nowUtc
                    )
                    .IsSuccess
            );
        }

        return payment;
    }

    private static Task<Result> RunStripeAsync(
        Error? verificationError = null,
        Error? parseError = null,
        IReadOnlyDictionary<string, string>? headers = null,
        FakeWebhookEventRepository? webhooks = null,
        FakePaymentAppMetrics? metrics = null
    ) =>
        ProcessProviderWebhookHandler.Handle(
            new ProcessProviderWebhookCommand(
                PaymentProviderCode.Stripe,
                "{}",
                headers ?? new Dictionary<string, string> { ["Stripe-Signature"] = "t=1,v1=test" }
            ),
            new FakePaymentAdapterFactory(
                new FakePaymentProvider(
                    PaymentProviderCode.Stripe,
                    new WebhookVerificationResult($"evt_{Guid.NewGuid():N}", "payment_intent.updated", "{}"),
                    Payload(PaymentStatus.Processing),
                    verificationError,
                    parseError
                )
            ),
            new FakeProviderWebhookSecrets(),
            webhooks ?? new FakeWebhookEventRepository(),
            new FakeSaaSPaymentRepository(CreateProcessingOffSessionPayment(SaaSPaymentType.SubscriptionRenewal)),
            new FakePaymentAuditLogWriter(),
            new FakeUnitOfWork(),
            metrics ?? new FakePaymentAppMetrics(),
            new FakePaymentAttemptThrottle(),
            new FakeCorrelationContext(),
            NullLogger<WebhookEvent>.Instance,
            CancellationToken.None
        );
}
