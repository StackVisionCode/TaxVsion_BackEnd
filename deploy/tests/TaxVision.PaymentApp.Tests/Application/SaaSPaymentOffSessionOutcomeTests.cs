using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.SaaSPayments.Common;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.ValueObjects;

namespace TaxVision.PaymentApp.Tests.Application;

/// <summary>Caracterización de PAY-7: cada resultado off-session y su transición, sus intentos y su reintento.</summary>
public sealed class SaaSPaymentOffSessionOutcomeTests
{
    private const string Reference = "pi_off_1";

    private readonly RecordingMetrics _metrics = new();

    [Fact]
    public void Succeeded_with_a_reference_charges_and_counts_one_attempt()
    {
        var payment = Renewal();

        Apply(payment, new(OffSessionChargeStatus.Succeeded, Reference, null, null));

        Assert.Equal(PaymentStatus.Succeeded, payment.Status);
        Assert.Equal(Reference, payment.ExternalChargeReference!.Value);
        Assert.Equal("Succeeded", Assert.Single(payment.Attempts).ProviderResponseCode);
        Assert.Equal(("Stripe", "SubscriptionRenewal"), Assert.Single(_metrics.Succeeded));
        Assert.Empty(_metrics.Failed);
    }

    [Fact]
    public void Succeeded_without_a_reference_fails_and_keeps_dunning()
    {
        var payment = Renewal();
        var before = DateTime.UtcNow;

        Apply(payment, new(OffSessionChargeStatus.Succeeded, null, null, null));

        Assert.Equal(PaymentStatus.Failed, payment.Status);
        Assert.Equal("OffSession.MissingReference", payment.FailureCode);
        Assert.Equal("Provider returned Succeeded with no reference.", payment.FailureReason);
        Assert.Single(payment.Attempts);
        AssertRetriesIn(payment, before, hours: 1);
        Assert.Empty(_metrics.Succeeded);
        Assert.Equal("OffSession.MissingReference", Assert.Single(_metrics.Failed).Code);
    }

    [Fact]
    public void RequiresUserAction_without_a_reference_fails_for_the_pay_now_banner()
    {
        var payment = Renewal();
        var before = DateTime.UtcNow;

        Apply(payment, new(OffSessionChargeStatus.RequiresUserAction, null, null, null));

        Assert.Equal(PaymentStatus.Failed, payment.Status);
        Assert.Equal("authentication_required", payment.FailureCode);
        Assert.Equal("Issuing bank requires customer authentication.", payment.FailureReason);
        AssertRetriesIn(payment, before, hours: 1);
    }

    [Fact]
    public void RequiresUserAction_keeps_the_provider_code_and_message()
    {
        var payment = Renewal();

        Apply(payment, new(OffSessionChargeStatus.RequiresUserAction, null, "sca_needed", "Bank asks."));

        Assert.Equal("sca_needed", payment.FailureCode);
        Assert.Equal("Bank asks.", payment.FailureReason);
    }

    [Fact]
    public void RequiresUserAction_with_a_reference_waits_for_the_payer()
    {
        var payment = Renewal();

        Apply(payment, new(OffSessionChargeStatus.RequiresUserAction, Reference, "sca_needed", null));

        Assert.Equal(PaymentStatus.RequiresAction, payment.Status);
        Assert.Equal("sca_needed", payment.NextActionType);
        Assert.Equal(string.Empty, payment.NextActionUrl);
        Assert.Single(payment.Attempts);
        Assert.Null(payment.NextRetryAtUtc);
        Assert.Empty(_metrics.Failed);
    }

    [Fact]
    public void RequiresUserAction_with_a_reference_defaults_the_action_type()
    {
        var payment = Renewal();

        Apply(payment, new(OffSessionChargeStatus.RequiresUserAction, Reference, null, null));

        Assert.Equal("authentication_required", payment.NextActionType);
    }

    [Theory]
    [InlineData(OffSessionChargeStatus.Processing)]
    [InlineData(OffSessionChargeStatus.OutcomeUnknown)]
    public void No_final_outcome_with_a_reference_stays_Processing(OffSessionChargeStatus status)
    {
        var payment = Renewal();

        Apply(payment, new(status, Reference, null, null));

        Assert.Equal(PaymentStatus.Processing, payment.Status);
        Assert.Single(payment.Attempts);
        Assert.Null(payment.NextRetryAtUtc);
        Assert.Empty(_metrics.Failed);
        Assert.Empty(_metrics.Succeeded);
    }

    [Theory]
    [InlineData(OffSessionChargeStatus.Processing)]
    [InlineData(OffSessionChargeStatus.OutcomeUnknown)]
    public void No_final_outcome_without_a_reference_stays_Pending_with_no_attempt(OffSessionChargeStatus status)
    {
        var payment = Renewal();

        Apply(payment, new(status, null, "stripe_error", "Unknown."));

        Assert.Equal(PaymentStatus.Pending, payment.Status);
        Assert.Empty(payment.Attempts);
        Assert.Null(payment.FailureCode);
        Assert.Null(payment.NextRetryAtUtc);
    }

    [Fact]
    public void Failed_without_a_reference_counts_one_attempt_and_retries_in_one_hour()
    {
        var payment = Renewal();
        var before = DateTime.UtcNow;

        Apply(payment, new(OffSessionChargeStatus.Failed, null, "card_declined", "Declined."));

        Assert.Equal(PaymentStatus.Failed, payment.Status);
        Assert.Equal("card_declined", payment.FailureCode);
        Assert.Equal("Declined.", payment.FailureReason);
        Assert.Single(payment.Attempts);
        AssertRetriesIn(payment, before, hours: 1);
    }

    [Fact]
    public void Failed_with_a_reference_counts_the_attempt_once()
    {
        var payment = Renewal();
        var before = DateTime.UtcNow;

        Apply(payment, new(OffSessionChargeStatus.Failed, Reference, "card_declined", "Declined."));

        Assert.Equal(PaymentStatus.Failed, payment.Status);
        Assert.Equal(Reference, payment.ExternalChargeReference!.Value);
        Assert.Single(payment.Attempts);
        AssertRetriesIn(payment, before, hours: 1);
    }

    [Fact]
    public void Failed_uses_the_default_texts()
    {
        var payment = Renewal();

        Apply(payment, new(OffSessionChargeStatus.Failed, null, null, null));

        Assert.Equal("Provider.Failed", payment.FailureCode);
        Assert.Equal("Off-session charge was declined.", payment.FailureReason);
    }

    [Theory]
    [InlineData("requires_payment_method")]
    [InlineData("REQUIRES_PAYMENT_METHOD")]
    [InlineData("PayPal.Authorize.NotImplemented")]
    public void Failed_with_a_permanent_code_never_retries(string code)
    {
        var payment = Renewal();

        Apply(payment, new(OffSessionChargeStatus.Failed, null, code, "No."));

        Assert.Equal(PaymentStatus.Failed, payment.Status);
        Assert.Null(payment.NextRetryAtUtc);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(Reference)]
    public void Failed_retries_follow_the_schedule_and_stop_after_the_fourth_attempt(string? reference)
    {
        var payment = Renewal();
        int?[] expected = [1, 6, 24, null];

        foreach (var hours in expected)
        {
            var before = DateTime.UtcNow;
            Apply(payment, new(OffSessionChargeStatus.Failed, reference, "card_declined", "Declined."));

            if (hours is { } due)
                AssertRetriesIn(payment, before, due);
            else
                Assert.Null(payment.NextRetryAtUtc);

            if (payment.NextRetryAtUtc is { } retryAt)
                Assert.True(payment.PrepareForRetry(Guid.Empty, retryAt).IsSuccess);
        }

        Assert.Equal(4, payment.Attempts.Count);
    }

    [Fact]
    public void Failed_on_a_type_without_dunning_does_not_retry()
    {
        var payment = Payment(SaaSPaymentType.PlanChangeCharge);

        Apply(payment, new(OffSessionChargeStatus.Failed, null, "card_declined", "Declined."));

        Assert.Null(payment.NextRetryAtUtc);
    }

    [Fact]
    public void A_reference_too_long_counts_as_no_reference()
    {
        var payment = Renewal();

        Apply(payment, new(OffSessionChargeStatus.Succeeded, new string('x', 201), null, null));

        Assert.Equal(PaymentStatus.Failed, payment.Status);
        Assert.Equal("OffSession.MissingReference", payment.FailureCode);
        Assert.Null(payment.ExternalChargeReference);
    }

    [Fact]
    public void Succeeded_on_a_payment_already_Processing_keeps_its_attempt_and_reference()
    {
        var payment = Renewal();
        Apply(payment, new(OffSessionChargeStatus.Processing, "pi_first", null, null));

        Apply(payment, new(OffSessionChargeStatus.Succeeded, "pi_second", null, null));

        Assert.Equal(PaymentStatus.Succeeded, payment.Status);
        Assert.Equal("pi_first", payment.ExternalChargeReference!.Value);
        Assert.Single(payment.Attempts);
    }

    // Hoy la transición inválida no se reporta: el pago sigue igual, pero la métrica de éxito se registra.
    [Fact]
    public void Succeeded_on_a_cancelled_payment_does_not_move_it_but_is_counted()
    {
        var payment = Renewal();
        Assert.True(payment.MarkCancelled("Superseded", DateTime.UtcNow).IsSuccess);

        Apply(payment, new(OffSessionChargeStatus.Succeeded, Reference, null, null));

        Assert.Equal(PaymentStatus.Cancelled, payment.Status);
        Assert.Null(payment.ExternalChargeReference);
        Assert.Single(_metrics.Succeeded);
    }

    private void Apply(SaaSPayment payment, OffSessionChargeOutcome outcome) =>
        SaaSPaymentOffSessionOutcome.Apply(payment, outcome, PaymentProviderCode.Stripe, _metrics, NullLogger.Instance);

    private static void AssertRetriesIn(SaaSPayment payment, DateTime before, int hours) =>
        Assert.InRange(payment.NextRetryAtUtc!.Value, before.AddHours(hours), DateTime.UtcNow.AddHours(hours));

    private static SaaSPayment Renewal() => Payment(SaaSPaymentType.SubscriptionRenewal);

    private static SaaSPayment Payment(SaaSPaymentType type) =>
        SaaSPayment
            .Create(
                Guid.NewGuid(),
                IdempotencyKey.Create($"off-{Guid.NewGuid():N}").Value,
                Money.Create(1999, "USD").Value,
                type,
                Guid.NewGuid(),
                PaymentProviderCode.Stripe,
                StatementDescriptor.Create("TAXVISION SAAS").Value,
                Guid.Empty,
                DateTime.UtcNow
            )
            .Value;

    private sealed class RecordingMetrics : IPaymentAppMetrics
    {
        public List<(string Provider, string Type)> Succeeded { get; } = [];
        public List<(string Provider, string Type, string Code)> Failed { get; } = [];

        public void RecordAttempted(string provider, string type) { }

        public void RecordSucceeded(string provider, string type) => Succeeded.Add((provider, type));

        public void RecordFailed(string provider, string type, string failureCode) =>
            Failed.Add((provider, type, failureCode));

        public void RecordRefunded(string provider) { }

        public void RecordChargedBack(string provider) { }

        public void RecordWebhookReceived(string provider) { }

        public void RecordWebhookDuplicate(string provider) { }

        public void RecordWebhookSignatureFailed(string provider) { }

        public void RecordProviderLatency(double milliseconds, string provider, string method) { }

        public void RecordPaymentReviewRequired(string provider, string reason) { }
    }
}
