using BuildingBlocks.Messaging.PaymentAppIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Application.SaaSPayments.Commands.ReconcileStuckSaaSPayment;
using TaxVision.PaymentApp.Domain.ProviderCustomers;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.ValueObjects;
using TaxVision.PaymentApp.Tests.TestDoubles;

namespace TaxVision.PaymentApp.Tests.Application;

/// <summary>Un pago atascado en Processing se resuelve en su propio comando: guarda y después publica.</summary>
public sealed class ReconcileStuckSaaSPaymentHandlerTests
{
    private readonly Guid _tenantId = Guid.NewGuid();

    [Fact]
    public async Task Provider_succeeded_marks_succeeded_and_publishes_once()
    {
        var payment = ProcessingRenewal(DateTime.UtcNow.AddMinutes(-5));
        var unitOfWork = new FakeUnitOfWork();
        var bus = new CapturingMessageBus();

        var result = await HandleAsync(payment, Succeeded(), unitOfWork, bus);

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentStatus.Succeeded, payment.Status);
        Assert.Single(bus.Published.OfType<SubscriptionRenewalPaymentSucceededIntegrationEvent>());
        Assert.Equal(1, unitOfWork.SaveCalls);
    }

    [Fact]
    public async Task Saves_before_publishing()
    {
        var payment = ProcessingRenewal(DateTime.UtcNow.AddMinutes(-5));
        var bus = new CapturingMessageBus();
        var unitOfWork = new PublishedCountAtSave(bus);

        await HandleAsync(payment, Succeeded(), unitOfWork, bus);

        Assert.Equal([0], unitOfWork.PublishedAtEachSave);
        Assert.NotEmpty(bus.Published);
    }

    [Fact]
    public async Task Payment_no_longer_processing_is_a_noop()
    {
        var payment = ProcessingRenewal(DateTime.UtcNow.AddMinutes(-5));
        payment.MarkSucceeded(DateTime.UtcNow, Guid.Empty);
        var provider = new StatusPaymentProvider(Succeeded());
        var unitOfWork = new FakeUnitOfWork();
        var bus = new CapturingMessageBus();

        var result = await HandleAsync(payment, provider, unitOfWork, bus);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, provider.StatusCalls);
        Assert.Empty(bus.Published);
        Assert.Equal(0, unitOfWork.SaveCalls);
    }

    [Fact]
    public async Task Payment_flagged_for_review_is_not_confirmed()
    {
        var payment = ProcessingRenewal(DateTime.UtcNow.AddMinutes(-5));
        payment.FlagForReview(SaaSPayment.ReviewReasonAmountMismatch, "pi_mismatch", 100, "usd", DateTime.UtcNow);
        var provider = new StatusPaymentProvider(Succeeded());
        var unitOfWork = new FakeUnitOfWork();
        var bus = new CapturingMessageBus();

        var result = await HandleAsync(payment, provider, unitOfWork, bus);

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentStatus.Processing, payment.Status);
        Assert.Equal(0, provider.StatusCalls);
        Assert.Empty(bus.Published);
        Assert.Equal(0, unitOfWork.SaveCalls);
    }

    [Fact]
    public async Task Provider_not_found_after_grace_marks_onboarding_failed_without_retry()
    {
        var payment = ProcessingOnboarding(DateTime.UtcNow.AddHours(-1));
        var unitOfWork = new FakeUnitOfWork();
        var bus = new CapturingMessageBus();

        var result = await HandleAsync(payment, NotFound(), unitOfWork, bus, tenantId: Guid.Empty);

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentStatus.Failed, payment.Status);
        Assert.Equal("PayPal.ChargeStatus.NotFound", payment.FailureCode);
        Assert.Null(payment.NextRetryAtUtc);
        Assert.Equal(1, unitOfWork.SaveCalls);
        var published = Assert.Single(bus.Published.OfType<OnboardingPaymentFailedIntegrationEvent>());
        Assert.Equal(payment.Id, published.SaaSPaymentId);
        Assert.Equal("PayPal.ChargeStatus.NotFound", published.FailureCode);
    }

    [Fact]
    public async Task Recent_provider_not_found_keeps_payment_processing()
    {
        var payment = ProcessingOnboarding(DateTime.UtcNow);
        var unitOfWork = new FakeUnitOfWork();
        var bus = new CapturingMessageBus();

        await HandleAsync(payment, NotFound(), unitOfWork, bus, tenantId: Guid.Empty);

        Assert.Equal(PaymentStatus.Processing, payment.Status);
        Assert.Empty(bus.Published);
        Assert.Equal(0, unitOfWork.SaveCalls);
    }

    private Task<Result> HandleAsync(
        SaaSPayment payment,
        Result<ChargeAuthorizationResult> providerResult,
        IUnitOfWork unitOfWork,
        CapturingMessageBus bus,
        Guid? tenantId = null
    ) => HandleAsync(payment, new StatusPaymentProvider(providerResult), unitOfWork, bus, tenantId);

    // El bus recibe lo que devuelve el handler del evento de dominio, que corre después del commit del comando.
    private async Task<Result> HandleAsync(
        SaaSPayment payment,
        StatusPaymentProvider provider,
        IUnitOfWork unitOfWork,
        CapturingMessageBus bus,
        Guid? tenantId = null
    )
    {
        var result = await ReconcileStuckSaaSPaymentHandler.Handle(
            new ReconcileStuckSaaSPaymentCommand(tenantId ?? _tenantId, payment.Id),
            new FakeSaaSPaymentRepository { ById = payment },
            new FakePaymentAdapterFactory(provider),
            unitOfWork,
            NullLogger<SaaSPayment>.Instance,
            CancellationToken.None
        );
        bus.Published.AddRange(await SaaSPaymentEventReplay.DispatchAsync(payment));
        return result;
    }

    private static Result<ChargeAuthorizationResult> Succeeded() =>
        Result.Success(new ChargeAuthorizationResult("pi_done", PaymentStatus.Succeeded));

    private static Result<ChargeAuthorizationResult> NotFound() =>
        Result.Failure<ChargeAuthorizationResult>(
            new Error("PayPal.ChargeStatus.NotFound", "PayPal returned HTTP 404.")
        );

    private SaaSPayment ProcessingRenewal(DateTime nowUtc)
    {
        var payment = SaaSPayment
            .Create(
                _tenantId,
                IdempotencyKey.Create($"subscription-renewal-{Guid.NewGuid():N}").Value,
                Money.Create(4900, "USD").Value,
                SaaSPaymentType.SubscriptionRenewal,
                Guid.NewGuid(),
                PaymentProviderCode.PayPal,
                StatementDescriptor.Create("TAXVISION SAAS").Value,
                Guid.Empty,
                nowUtc
            )
            .Value;
        payment.MarkProcessing(
            ExternalPaymentReference.Create(PaymentProviderCode.PayPal, "pi_stuck").Value,
            "processing",
            null,
            Guid.Empty,
            nowUtc
        );
        return payment;
    }

    private static SaaSPayment ProcessingOnboarding(DateTime nowUtc)
    {
        var payment = SaaSPayment
            .CreateForOnboarding(
                Guid.NewGuid(),
                IdempotencyKey.Create($"onboarding-{Guid.NewGuid():N}").Value,
                Money.Create(4900, "USD").Value,
                Guid.NewGuid(),
                PaymentProviderCode.PayPal,
                StatementDescriptor.Create("TAXVISION SAAS").Value,
                nowUtc
            )
            .Value;
        payment.RecordHostedCheckoutSession(
            "ORDER-404",
            ExternalPaymentReference.Create(PaymentProviderCode.PayPal, "ORDER-404").Value,
            "https://paypal.example/checkout",
            nowUtc
        );
        return payment;
    }

    // Anota cuántos eventos había publicados en cada guardado.
    private sealed class PublishedCountAtSave(CapturingMessageBus bus) : IUnitOfWork
    {
        public List<int> PublishedAtEachSave { get; } = [];

        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            PublishedAtEachSave.Add(bus.Published.Count);
            return Task.FromResult(1);
        }
    }
}
