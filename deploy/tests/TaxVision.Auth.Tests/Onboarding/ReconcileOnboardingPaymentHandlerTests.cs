using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Messaging.PaymentAppIntegrationEvents;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Auth.Application.Onboarding;
using TaxVision.Auth.Application.Onboarding.Abstractions;
using TaxVision.Auth.Application.Onboarding.TenantOnboardings.Commands;
using TaxVision.Auth.Application.Onboarding.TenantOnboardings.Services;
using TaxVision.Auth.Domain.Onboarding.TenantOnboardings;
using TaxVision.Auth.Domain.Onboarding.ValueObjects;
using TaxVision.Auth.Infrastructure.Security;
using TaxVision.Auth.Tests.Application;

namespace TaxVision.Auth.Tests.Onboarding;

public sealed class ReconcileOnboardingPaymentHandlerTests
{
    private static readonly OnboardingOptions RegistrationOptions = new()
    {
        RegistrationUrlBase = "https://app.example.com",
    };

    [Fact]
    public async Task Succeeded_payment_completes_onboarding_and_returns_registration_url()
    {
        var now = DateTime.UtcNow;
        var onboarding = CreatePaymentProcessingOnboarding(now, out var paymentId);
        var onboardings = new FakeTenantOnboardingRepository { Existing = onboarding };
        var paymentApp = PaymentAppWithReconcile(
            new PaymentAppReconcileResult(
                paymentId,
                OnboardingPaymentStatus.Succeeded,
                4900,
                "USD",
                FailureCode: null,
                FailureMessage: null,
                ProviderPaymentReference: "CAPTURE-123",
                PaidAtUtc: now
            )
        );
        var tokenReferences = new FakeTokenReferenceStore();
        var bus = new FakeMessageBus();
        var unitOfWork = new FakeUnitOfWork();

        var result = await ReconcileOnboardingPaymentHandler.Handle(
            new ReconcileOnboardingPaymentCommand(onboarding.Id),
            onboardings,
            paymentApp,
            BuildCompleter(tokenReferences, bus),
            new FakePlanCatalogClient("Enterprise"),
            unitOfWork,
            new FakeCorrelationContext(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(TenantOnboardingStatus.RegistrationPending.ToString(), result.Value.Status);
        Assert.StartsWith("https://app.example.com/register?token=", result.Value.RegistrationUrl);
        Assert.Equal(paymentId, paymentApp.LastReconcileRequest!.PaymentId);
        Assert.Equal(TenantOnboardingStatus.RegistrationPending, onboarding.Status);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
        Assert.NotNull(tokenReferences.Stored);
        Assert.Contains(bus.Published, message => message is OnboardingRegistrationReadyIntegrationEvent);
        Assert.Contains(bus.Published, message => message is OnboardingFinalizeCommand);
        Assert.Contains(bus.Published, message => message is RequestOnboardingReceiptCommand);
    }

    [Fact]
    public async Task Processing_payment_returns_current_state_without_persisting()
    {
        var onboarding = CreatePaymentProcessingOnboarding(DateTime.UtcNow, out var paymentId);
        var paymentApp = PaymentAppWithReconcile(
            new PaymentAppReconcileResult(
                paymentId,
                OnboardingPaymentStatus.Processing,
                4900,
                "USD",
                FailureCode: null,
                FailureMessage: null,
                ProviderPaymentReference: null,
                PaidAtUtc: null
            )
        );
        var unitOfWork = new FakeUnitOfWork();
        var bus = new FakeMessageBus();

        var result = await ReconcileOnboardingPaymentHandler.Handle(
            new ReconcileOnboardingPaymentCommand(onboarding.Id),
            new FakeTenantOnboardingRepository { Existing = onboarding },
            paymentApp,
            BuildCompleter(new FakeTokenReferenceStore(), bus),
            new FakePlanCatalogClient("Enterprise"),
            unitOfWork,
            new FakeCorrelationContext(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(TenantOnboardingStatus.PaymentProcessing.ToString(), result.Value.Status);
        Assert.Null(result.Value.RegistrationUrl);
        Assert.Equal(paymentId, paymentApp.LastReconcileRequest!.PaymentId);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task Failed_payment_marks_onboarding_failed_for_immediate_retry_ux()
    {
        var onboarding = CreatePaymentProcessingOnboarding(DateTime.UtcNow, out var paymentId);
        var paymentApp = PaymentAppWithReconcile(
            new PaymentAppReconcileResult(
                paymentId,
                OnboardingPaymentStatus.Failed,
                4900,
                "USD",
                "PayPal.DECLINED",
                "PayPal declined the capture.",
                ProviderPaymentReference: null,
                PaidAtUtc: null
            )
        );
        var unitOfWork = new FakeUnitOfWork();
        var bus = new FakeMessageBus();

        var result = await ReconcileOnboardingPaymentHandler.Handle(
            new ReconcileOnboardingPaymentCommand(onboarding.Id),
            new FakeTenantOnboardingRepository { Existing = onboarding },
            paymentApp,
            BuildCompleter(new FakeTokenReferenceStore(), bus),
            new FakePlanCatalogClient("Enterprise"),
            unitOfWork,
            new FakeCorrelationContext(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(TenantOnboardingStatus.PaymentFailed.ToString(), result.Value.Status);
        Assert.Equal("PayPal.DECLINED", result.Value.FailureCode);
        Assert.Equal("PayPal declined the capture.", result.Value.FailureMessage);
        Assert.Equal(TenantOnboardingStatus.PaymentFailed, onboarding.Status);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task Refunded_payment_marks_onboarding_failed_instead_of_staying_processing()
    {
        var onboarding = CreatePaymentProcessingOnboarding(DateTime.UtcNow, out var paymentId);
        var paymentApp = PaymentAppWithReconcile(
            new PaymentAppReconcileResult(
                paymentId,
                OnboardingPaymentStatus.Refunded,
                4900,
                "USD",
                "PayPal.Refunded",
                "The payment was refunded before registration.",
                ProviderPaymentReference: null,
                PaidAtUtc: null
            )
        );
        var unitOfWork = new FakeUnitOfWork();
        var bus = new FakeMessageBus();

        var result = await ReconcileOnboardingPaymentHandler.Handle(
            new ReconcileOnboardingPaymentCommand(onboarding.Id),
            new FakeTenantOnboardingRepository { Existing = onboarding },
            paymentApp,
            BuildCompleter(new FakeTokenReferenceStore(), bus),
            new FakePlanCatalogClient("Enterprise"),
            unitOfWork,
            new FakeCorrelationContext(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(TenantOnboardingStatus.PaymentFailed.ToString(), result.Value.Status);
        Assert.Equal("PayPal.Refunded", result.Value.FailureCode);
        Assert.Equal(TenantOnboardingStatus.PaymentFailed, onboarding.Status);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task Registration_pending_replay_returns_existing_url_without_reissuing_token_or_calling_paymentapp()
    {
        var onboarding = CreatePaymentProcessingOnboarding(DateTime.UtcNow, out _);
        Assert.True(
            onboarding.MarkPaymentCompleted(onboarding.PaymentId!.Value.ToString("N"), DateTime.UtcNow).IsSuccess
        );
        var tokenReferences = new FakeTokenReferenceStore { ToPeek = "existing-registration-token" };
        Assert.True(
            onboarding
                .SetRegistrationToken(
                    RegistrationTokenHash.Create(new string('a', 64)).Value,
                    DateTime.UtcNow.AddHours(72),
                    tokenReferences.Reference
                )
                .IsSuccess
        );
        var paymentApp = PaymentAppWithReconcile(
            new PaymentAppReconcileResult(
                onboarding.PaymentId.Value,
                OnboardingPaymentStatus.Succeeded,
                4900,
                "USD",
                FailureCode: null,
                FailureMessage: null,
                ProviderPaymentReference: "CAPTURE-123",
                PaidAtUtc: DateTime.UtcNow
            )
        );
        var bus = new FakeMessageBus();
        var unitOfWork = new FakeUnitOfWork();

        var result = await ReconcileOnboardingPaymentHandler.Handle(
            new ReconcileOnboardingPaymentCommand(onboarding.Id),
            new FakeTenantOnboardingRepository { Existing = onboarding },
            paymentApp,
            BuildCompleter(tokenReferences, bus),
            new FakePlanCatalogClient("Enterprise"),
            unitOfWork,
            new FakeCorrelationContext(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(TenantOnboardingStatus.RegistrationPending.ToString(), result.Value.Status);
        Assert.Equal(
            "https://app.example.com/register?token=existing-registration-token",
            result.Value.RegistrationUrl
        );
        Assert.Null(paymentApp.LastReconcileRequest);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
        Assert.Empty(bus.Published);
    }

    private static TenantOnboarding CreatePaymentProcessingOnboarding(DateTime now, out Guid paymentId)
    {
        var onboarding = OnboardingTestFactory.NewOnboarding(now);
        paymentId = Guid.NewGuid();
        Assert.True(onboarding.MarkPaymentProcessing(paymentId, paymentId.ToString("N")).IsSuccess);
        return onboarding;
    }

    private static FakePaymentAppOnboardingClient PaymentAppWithReconcile(PaymentAppReconcileResult result) =>
        new(
            Result.Success(
                new PaymentAppCheckoutResult(Guid.NewGuid(), "https://checkout.example.com", "session", DateTime.UtcNow)
            )
        )
        {
            ReconcileResult = Result.Success(result),
        };

    private static OnboardingSuccessCompleter BuildCompleter(
        FakeTokenReferenceStore tokenReferences,
        FakeMessageBus bus
    ) =>
        new(
            new SecureTokenService(),
            tokenReferences,
            Options.Create(RegistrationOptions),
            bus,
            NullLogger<OnboardingSuccessCompleter>.Instance
        );
}
