using BuildingBlocks.Messaging.AuthIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.PaymentApp.Application.Onboarding.IntegrationEvents;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.ValueObjects;
using TaxVision.PaymentApp.Tests.TestDoubles;

namespace TaxVision.PaymentApp.Tests.Application;

/// <summary>El reembolso del onboarding se valida antes de llamar al proveedor; si no procede, va a DLQ.</summary>
public sealed class OnboardingRefundConsumerTests
{
    [Fact]
    public async Task Legal_hold_does_not_call_the_provider_and_is_not_swallowed()
    {
        var payment = SucceededOnboardingPayment();
        payment.SetLegalHold(true, Guid.Empty, DateTime.UtcNow);
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

    private static Task HandleAsync(SaaSPayment payment, RefundRecordingProvider provider) =>
        OnboardingRefundConsumer.Handle(
            new OnboardingRefundRequestedIntegrationEvent
            {
                OnboardingId = payment.OnboardingId!.Value,
                PaymentId = payment.Id,
                Reason = "admin approved",
            },
            new FakeSaaSPaymentRepository { ByIdAnyTenant = payment },
            new FakePaymentAdapterFactory(provider),
            new FakePaymentAuditLogWriter(),
            new FakeUnitOfWork(),
            new FakePaymentAppMetrics(),
            new FakeCorrelationContext(),
            NullLogger<SaaSPayment>.Instance,
            CancellationToken.None
        );

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
