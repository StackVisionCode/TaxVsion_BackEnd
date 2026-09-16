using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Messaging.PaymentAppIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Auth.Application.Onboarding;
using TaxVision.Auth.Application.Onboarding.Consumers;
using TaxVision.Auth.Domain.Onboarding.TenantOnboardings;
using TaxVision.Auth.Tests.Application;

namespace TaxVision.Auth.Tests.Onboarding;

public sealed class OnboardingPaymentFailedConsumerTests
{
    private static readonly OnboardingOptions Options_ = new() { RegistrationUrlBase = "https://app.example.com" };

    [Fact]
    public async Task Marks_payment_failed_and_publishes_the_retry_notification()
    {
        var onboarding = OnboardingTestFactory.NewOnboarding(DateTime.UtcNow);
        var paymentId = Guid.NewGuid();
        onboarding.MarkPaymentProcessing(paymentId, paymentId.ToString("N"));
        var onboardings = new FakeTenantOnboardingRepository { Existing = onboarding };
        var bus = new FakeMessageBus();
        var unitOfWork = new FakeUnitOfWork();

        await OnboardingPaymentFailedConsumer.Handle(
            new OnboardingPaymentFailedIntegrationEvent
            {
                OnboardingId = onboarding.Id,
                SaaSPaymentId = paymentId,
                FailureCode = "card_declined",
                FailureReason = "Your card was declined.",
            },
            onboardings,
            bus,
            new FakePlanCatalogClient("Pro"),
            new FakeOnboardingReturnReferenceStore(),
            Options.Create(Options_),
            unitOfWork,
            new FakeCorrelationContext(),
            NullLogger<OnboardingPaymentFailedIntegrationEvent>.Instance,
            CancellationToken.None
        );

        Assert.Equal(TenantOnboardingStatus.PaymentFailed, onboarding.Status);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);

        var notice = Assert.Single(
            bus.Published.OfType<OnboardingPaymentFailedNotificationRequestedIntegrationEvent>()
        );
        Assert.Equal(onboarding.Id, notice.OnboardingId);
        Assert.Equal(onboarding.Email, notice.Email);
        Assert.Equal("Pro", notice.PlanName);
        Assert.Equal("Your card was declined.", notice.FailureReason);
        Assert.StartsWith("https://app.example.com/register?plan=", notice.RetryUrl);
        Assert.Contains(onboarding.PlanId.ToString(), notice.RetryUrl);
    }

    [Fact]
    public async Task Ignores_unknown_onboardings()
    {
        var onboardings = new FakeTenantOnboardingRepository();
        var bus = new FakeMessageBus();
        var unitOfWork = new FakeUnitOfWork();

        await OnboardingPaymentFailedConsumer.Handle(
            new OnboardingPaymentFailedIntegrationEvent
            {
                OnboardingId = Guid.NewGuid(),
                SaaSPaymentId = Guid.NewGuid(),
                FailureCode = "card_declined",
                FailureReason = "Declined.",
            },
            onboardings,
            bus,
            new FakePlanCatalogClient("Pro"),
            new FakeOnboardingReturnReferenceStore(),
            Options.Create(Options_),
            unitOfWork,
            new FakeCorrelationContext(),
            NullLogger<OnboardingPaymentFailedIntegrationEvent>.Instance,
            CancellationToken.None
        );

        Assert.Empty(bus.Published);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Does_not_notify_when_the_state_transition_is_rejected()
    {
        // PendingPayment (no PaymentProcessing) → MarkPaymentFailed falla → nada que notificar.
        var onboarding = OnboardingTestFactory.NewOnboarding(DateTime.UtcNow);
        var onboardings = new FakeTenantOnboardingRepository { Existing = onboarding };
        var bus = new FakeMessageBus();
        var unitOfWork = new FakeUnitOfWork();

        await OnboardingPaymentFailedConsumer.Handle(
            new OnboardingPaymentFailedIntegrationEvent
            {
                OnboardingId = onboarding.Id,
                SaaSPaymentId = Guid.NewGuid(),
                FailureCode = "card_declined",
                FailureReason = "Declined.",
            },
            onboardings,
            bus,
            new FakePlanCatalogClient("Pro"),
            new FakeOnboardingReturnReferenceStore(),
            Options.Create(Options_),
            unitOfWork,
            new FakeCorrelationContext(),
            NullLogger<OnboardingPaymentFailedIntegrationEvent>.Instance,
            CancellationToken.None
        );

        Assert.Empty(bus.Published);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }
}
