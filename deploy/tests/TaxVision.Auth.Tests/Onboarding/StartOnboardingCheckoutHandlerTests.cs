using BuildingBlocks.Results;
using TaxVision.Auth.Application.Onboarding.Abstractions;
using TaxVision.Auth.Application.Onboarding.TenantOnboardings.Commands;
using TaxVision.Auth.Domain.Onboarding.TenantOnboardings;
using TaxVision.Auth.Tests.Application;

namespace TaxVision.Auth.Tests.Onboarding;

/// <summary>PayFlow Fase 9 — llama al checkout M2M de PaymentApp y avanza el onboarding a
/// PaymentProcessing usando el PaymentId como referencia estable entre ambos servicios.</summary>
public sealed class StartOnboardingCheckoutHandlerTests
{
    [Fact]
    public async Task Marks_payment_processing_using_the_paymentapp_paymentid_as_reference()
    {
        var now = DateTime.UtcNow;
        var onboarding = OnboardingTestFactory.NewOnboarding(now);
        var onboardings = new FakeTenantOnboardingRepository { Existing = onboarding };
        var paymentId = Guid.NewGuid();
        var checkoutResult = Result.Success(
            new PaymentAppCheckoutResult(paymentId, "https://checkout.example.com/session", "sess_123", now.AddHours(1))
        );
        var paymentApp = new FakePaymentAppOnboardingClient(checkoutResult);
        var unitOfWork = new FakeUnitOfWork();

        var result = await StartOnboardingCheckoutHandler.Handle(
            new StartOnboardingCheckoutCommand(
                onboarding.Id,
                "buyer@example.com",
                "https://app.example.com/success",
                "https://app.example.com/cancel"
            ),
            onboardings,
            paymentApp,
            unitOfWork,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(paymentId, result.Value.PaymentId);
        Assert.Equal(TenantOnboardingStatus.PaymentProcessing, onboarding.Status);
        Assert.Equal(paymentId.ToString("N"), onboarding.PaymentReference);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Fails_without_persisting_when_the_onboarding_does_not_exist()
    {
        var onboardings = new FakeTenantOnboardingRepository();
        var paymentApp = new FakePaymentAppOnboardingClient(
            Result.Success(new PaymentAppCheckoutResult(Guid.NewGuid(), "url", "sess", DateTime.UtcNow))
        );
        var unitOfWork = new FakeUnitOfWork();

        var result = await StartOnboardingCheckoutHandler.Handle(
            new StartOnboardingCheckoutCommand(
                Guid.NewGuid(),
                "buyer@example.com",
                "https://app.example.com/success",
                "https://app.example.com/cancel"
            ),
            onboardings,
            paymentApp,
            unitOfWork,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Onboarding.NotFound", result.Error.Code);
        Assert.Null(paymentApp.LastRequest);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Fails_without_persisting_when_paymentapp_call_fails()
    {
        var now = DateTime.UtcNow;
        var onboarding = OnboardingTestFactory.NewOnboarding(now);
        var onboardings = new FakeTenantOnboardingRepository { Existing = onboarding };
        var paymentApp = new FakePaymentAppOnboardingClient(
            Result.Failure<PaymentAppCheckoutResult>(new Error("PaymentAppClient.RequestFailed", "boom"))
        );
        var unitOfWork = new FakeUnitOfWork();

        var result = await StartOnboardingCheckoutHandler.Handle(
            new StartOnboardingCheckoutCommand(
                onboarding.Id,
                "buyer@example.com",
                "https://app.example.com/success",
                "https://app.example.com/cancel"
            ),
            onboardings,
            paymentApp,
            unitOfWork,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("PaymentAppClient.RequestFailed", result.Error.Code);
        Assert.Equal(TenantOnboardingStatus.PendingPayment, onboarding.Status);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }
}
