using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Auth.Application.Onboarding;
using TaxVision.Auth.Application.Onboarding.Abstractions;
using TaxVision.Auth.Application.Onboarding.TenantOnboardings.Commands;
using TaxVision.Auth.Application.Onboarding.TenantOnboardings.Services;
using TaxVision.Auth.Domain.Onboarding.TenantOnboardings;
using TaxVision.Auth.Infrastructure.Security;
using TaxVision.Auth.Tests.Application;

namespace TaxVision.Auth.Tests.Onboarding;

/// <summary>PayFlow Fase 9 + Gift/Referral — sin códigos, el checkout llama a PaymentApp y avanza a
/// PaymentProcessing (comportamiento base intacto). El reserver/completer se inyectan pero no se invocan
/// en el camino sin código.</summary>
public sealed class StartOnboardingCheckoutHandlerTests
{
    private static readonly OnboardingOptions RegistrationOptions = new()
    {
        RegistrationUrlBase = "https://app.example.com",
    };

    // Camino sin código: estos colaboradores nunca se invocan; fallan ruidosamente si se los llamara.
    private static OnboardingCodeReserver BuildReserver() =>
        new(new ThrowingPricingClient(), new ThrowingGrowthClient());

    private static OnboardingSuccessCompleter BuildCompleter() =>
        new(
            new SecureTokenService(),
            new FakeTokenReferenceStore(),
            Options.Create(RegistrationOptions),
            new FakeMessageBus(),
            NullLogger<OnboardingSuccessCompleter>.Instance
        );

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
            BuildReserver(),
            BuildCompleter(),
            new FakePlanCatalogClient("Enterprise"),
            paymentApp,
            unitOfWork,
            new FakeCorrelationContext(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(paymentId, result.Value.PaymentId);
        Assert.False(result.Value.FullyCovered);
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
            BuildReserver(),
            BuildCompleter(),
            new FakePlanCatalogClient("Enterprise"),
            paymentApp,
            unitOfWork,
            new FakeCorrelationContext(),
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
            BuildReserver(),
            BuildCompleter(),
            new FakePlanCatalogClient("Enterprise"),
            paymentApp,
            unitOfWork,
            new FakeCorrelationContext(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("PaymentAppClient.RequestFailed", result.Error.Code);
        Assert.Equal(TenantOnboardingStatus.PendingPayment, onboarding.Status);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    private sealed class ThrowingPricingClient : IOnboardingPlanPricingClient
    {
        public Task<Result<OnboardingPlanPrice>> GetGrossPriceAsync(Guid planId, CancellationToken ct = default) =>
            throw new InvalidOperationException("Pricing must not be called in the no-code path.");
    }

    private sealed class ThrowingGrowthClient : IGrowthOnboardingClient
    {
        public Task<Result<GrowthQuoteResult>> QuoteAsync(GrowthQuoteRequest request, CancellationToken ct = default) =>
            throw new InvalidOperationException("Growth must not be called in the no-code path.");

        public Task<Result<GrowthReserveResult>> ReserveAsync(
            Guid quoteId,
            Guid onboardingId,
            int ttlSeconds,
            string idempotencyKey,
            CancellationToken ct = default
        ) => throw new InvalidOperationException("Growth must not be called in the no-code path.");

        public Task<Result> CommitAsync(
            Guid reservationId,
            Guid onboardingId,
            string snapshotHash,
            Guid sourceEventId,
            string idempotencyKey,
            CancellationToken ct = default
        ) => throw new InvalidOperationException("Growth must not be called in the no-code path.");

        public Task<Result> CancelAsync(
            Guid reservationId,
            Guid onboardingId,
            string reason,
            string idempotencyKey,
            CancellationToken ct = default
        ) => throw new InvalidOperationException("Growth must not be called in the no-code path.");

        public Task<Result> QualifyReferralAsync(GrowthQualifyRequest request, CancellationToken ct = default) =>
            throw new InvalidOperationException("Growth must not be called in the no-code path.");
    }
}
