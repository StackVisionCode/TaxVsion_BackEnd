using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Messaging.PaymentAppIntegrationEvents;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Auth.Application.Onboarding;
using TaxVision.Auth.Application.Onboarding.Consumers;
using TaxVision.Auth.Domain.Onboarding.TenantOnboardings;
using TaxVision.Auth.Infrastructure.Security;
using TaxVision.Auth.Tests.Application;

namespace TaxVision.Auth.Tests.Onboarding;

/// <summary>PayFlow Fase 9 — UoW #3: cierra el ciclo pago→token. Genera el RegistrationToken,
/// guarda solo su hash, y publica el evento que Notification (Fase 12) consumirá.</summary>
public sealed class OnboardingPaymentSucceededConsumerTests
{
    private static readonly OnboardingOptions RegistrationOptions = new()
    {
        RegistrationUrlBase = "https://app.example.com",
    };

    [Fact]
    public async Task Completes_payment_and_publishes_registration_ready_with_a_stored_raw_token()
    {
        var now = DateTime.UtcNow;
        var onboarding = OnboardingTestFactory.NewOnboarding(now);
        var paymentId = Guid.NewGuid();
        Assert.True(onboarding.MarkPaymentProcessing(paymentId, paymentId.ToString("N")).IsSuccess);

        var onboardings = new FakeTenantOnboardingRepository { Existing = onboarding };
        var tokens = new SecureTokenService();
        var tokenReferences = new FakeTokenReferenceStore();
        var receiptDocuments = new FakeReceiptDocumentClient(Result.Success());
        var unitOfWork = new FakeUnitOfWork();
        var bus = new FakeMessageBus();
        var correlation = new FakeCorrelationContext();

        var evt = new OnboardingPaymentSucceededIntegrationEvent
        {
            OnboardingId = onboarding.Id,
            SaaSPaymentId = paymentId,
            PlanId = onboarding.PlanId,
            AmountPaidCents = 4900,
            Currency = "USD",
            PaidAtUtc = now,
            ProviderPaymentReference = "pi_123",
            PaymentMethodMasked = "Visa •••• 4242",
        };

        await OnboardingPaymentSucceededConsumer.Handle(
            evt,
            onboardings,
            tokens,
            tokenReferences,
            Options.Create(RegistrationOptions),
            receiptDocuments,
            unitOfWork,
            bus,
            correlation,
            NullLogger<OnboardingPaymentSucceededIntegrationEvent>.Instance,
            CancellationToken.None
        );

        Assert.Equal(TenantOnboardingStatus.RegistrationPending, onboarding.Status);
        Assert.NotNull(tokenReferences.Stored);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);

        var published = Assert.Single(bus.Published);
        var ready = Assert.IsType<OnboardingRegistrationReadyIntegrationEvent>(published);
        Assert.Equal(onboarding.Id, ready.OnboardingId);
        Assert.Equal(tokenReferences.Reference, ready.TokenReference);
        Assert.Equal("49.00 USD", ready.PriceFormatted);

        Assert.NotNull(receiptDocuments.LastRequest);
        Assert.Equal(onboarding.Id, receiptDocuments.LastRequest!.OnboardingId);
        Assert.Equal("_123", receiptDocuments.LastRequest.TransactionReferenceMask);
        Assert.Equal("Visa •••• 4242", receiptDocuments.LastRequest.PaymentMethodMasked);
        Assert.Equal($"onboarding-receipt-{onboarding.Id:N}", receiptDocuments.LastRequest.IdempotencyKey);
    }

    [Fact]
    public async Task Ignores_events_for_unknown_onboardings()
    {
        var onboardings = new FakeTenantOnboardingRepository();
        var tokens = new SecureTokenService();
        var tokenReferences = new FakeTokenReferenceStore();
        var receiptDocuments = new FakeReceiptDocumentClient(Result.Success());
        var unitOfWork = new FakeUnitOfWork();
        var bus = new FakeMessageBus();
        var correlation = new FakeCorrelationContext();

        var evt = new OnboardingPaymentSucceededIntegrationEvent
        {
            OnboardingId = Guid.NewGuid(),
            SaaSPaymentId = Guid.NewGuid(),
            PlanId = Guid.NewGuid(),
            AmountPaidCents = 4900,
            Currency = "USD",
            PaidAtUtc = DateTime.UtcNow,
            ProviderPaymentReference = "pi_123",
        };

        await OnboardingPaymentSucceededConsumer.Handle(
            evt,
            onboardings,
            tokens,
            tokenReferences,
            Options.Create(RegistrationOptions),
            receiptDocuments,
            unitOfWork,
            bus,
            correlation,
            NullLogger<OnboardingPaymentSucceededIntegrationEvent>.Instance,
            CancellationToken.None
        );

        Assert.Empty(bus.Published);
        Assert.Null(tokenReferences.Stored);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
        Assert.Null(receiptDocuments.LastRequest);
    }
}
