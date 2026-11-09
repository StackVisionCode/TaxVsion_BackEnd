using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.Onboarding.IntegrationEvents;
using TaxVision.PaymentApp.Domain.Audit;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.ValueObjects;
using TaxVision.PaymentApp.Tests.TestDoubles;

namespace TaxVision.PaymentApp.Tests.Application;

/// <summary>El reembolso del onboarding se valida antes de llamar al proveedor y ningún fallo se pierde: lo pasajero se
/// reintenta, el rechazo queda en revisión y lo reembolsado sin registro se concilia sin reenviar.</summary>
public sealed class OnboardingRefundConsumerTests
{
    [Fact]
    public async Task Legal_hold_does_not_call_the_provider_and_is_not_swallowed()
    {
        var payment = SucceededOnboardingPayment();
        payment.SetLegalHold(true, Guid.NewGuid(), DateTime.UtcNow);
        var provider = new RefundRecordingProvider();

        await Assert.ThrowsAsync<ArgumentException>(() => HandleAsync(payment, provider));

        Assert.Equal(0, provider.RefundCalls);
    }

    [Fact]
    public async Task Refund_uses_the_onboarding_key()
    {
        var payment = SucceededOnboardingPayment();
        var provider = new RefundRecordingProvider();

        await HandleAsync(payment, provider);

        Assert.Equal($"refund:onboarding:{payment.Id:N}", Assert.Single(provider.Keys));
        Assert.Equal(PaymentStatus.Refunded, payment.Status);
    }

    [Fact]
    public async Task Transient_provider_failure_is_retried_not_swallowed()
    {
        var payment = SucceededOnboardingPayment();
        var provider = new RefundRecordingProvider
        {
            FailWith = new Error("Stripe.Refund.Unavailable", "Stripe returned 503."),
        };

        // Excepción no permanente: la política estándar la reintenta y termina en la cola de errores.
        var thrown = await Assert.ThrowsAnyAsync<Exception>(() => HandleAsync(payment, provider));

        Assert.IsNotAssignableFrom<ArgumentException>(thrown);
        Assert.Equal(PaymentStatus.Succeeded, payment.Status);
    }

    [Fact]
    public async Task Definitive_provider_rejection_is_flagged_for_review_and_audited()
    {
        var payment = SucceededOnboardingPayment();
        var provider = new RefundRecordingProvider
        {
            FailWith = new Error("Stripe.Refund.Failed", "Charge already disputed."),
        };
        var audit = new RecordingAuditWriter();

        await HandleAsync(payment, provider, audit);

        Assert.Equal(SaaSPayment.ReviewReasonRefundRejected, payment.ReviewReasonCode);
        Assert.Equal(PaymentStatus.Succeeded, payment.Status);
        Assert.Contains(audit.Entries, e => e.Action == PaymentAuditAction.SaaSPaymentRefundRejected);
    }

    [Fact]
    public async Task Provider_refunded_but_domain_failed_is_flagged_and_not_refunded_again()
    {
        var payment = SucceededOnboardingPayment();
        var provider = new RefundRecordingProvider();
        // Mientras el proveedor reembolsa, el pago queda retenido: el dominio ya no acepta el reembolso.
        provider.DuringRefund = () => payment.SetLegalHold(true, Guid.NewGuid(), DateTime.UtcNow);

        await HandleAsync(payment, provider);
        provider.DuringRefund = null;
        payment.SetLegalHold(false, Guid.NewGuid(), DateTime.UtcNow);
        await HandleAsync(payment, provider);

        Assert.Equal(SaaSPayment.ReviewReasonRefundNeedsReconciliation, payment.ReviewReasonCode);
        Assert.Equal(1, provider.RefundCalls);
    }

    private sealed class RecordingAuditWriter : IPaymentAuditLogWriter
    {
        public List<PaymentAuditEntry> Entries { get; } = [];

        public Task AppendAsync(PaymentAuditEntry entry, CancellationToken ct = default)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }

    private static Task HandleAsync(
        SaaSPayment payment,
        RefundRecordingProvider provider,
        RecordingAuditWriter? audit = null
    )
    {
        var payments = new FakeSaaSPaymentRepository { ByIdAnyTenant = payment };
        var adapters = new FakePaymentAdapterFactory(provider);
        var unitOfWork = new FakeUnitOfWork();
        return OnboardingRefundConsumer.Handle(
            new OnboardingRefundRequestedIntegrationEvent
            {
                OnboardingId = payment.OnboardingId!.Value,
                PaymentId = payment.Id,
                Reason = "admin approved",
            },
            TestRefundExecution.Bus(() => (payments, unitOfWork), adapters, audit),
            adapters,
            new FakeCorrelationContext(),
            NullLogger<SaaSPayment>.Instance,
            CancellationToken.None
        );
    }

    private static SaaSPayment SucceededOnboardingPayment()
    {
        var nowUtc = DateTime.UtcNow;
        var payment = SaaSPayment
            .CreateForOnboarding(
                Guid.NewGuid(),
                IdempotencyKey.Create($"onboarding-{Guid.NewGuid():N}").Value,
                Money.Create(4900, "USD").Value,
                Guid.NewGuid(),
                PaymentProviderCode.Stripe,
                StatementDescriptor.Create("TAXVISION SAAS").Value,
                nowUtc
            )
            .Value;
        payment.MarkProcessing(
            ExternalPaymentReference.Create(PaymentProviderCode.Stripe, "pi_onboarding").Value,
            "processing",
            null,
            Guid.Empty,
            nowUtc
        );
        payment.MarkSucceeded(nowUtc, Guid.Empty);
        return payment;
    }
}
