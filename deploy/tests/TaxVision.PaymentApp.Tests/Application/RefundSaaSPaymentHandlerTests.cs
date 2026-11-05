using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Application.SaaSPayments.Commands.RefundSaaSPayment;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.ValueObjects;
using TaxVision.PaymentApp.Tests.TestDoubles;

namespace TaxVision.PaymentApp.Tests.Application;

/// <summary>El reembolso se valida antes de llamar al proveedor y viaja con una clave de idempotencia estable.</summary>
public sealed class RefundSaaSPaymentHandlerTests
{
    private static readonly Guid PaymentId = Guid.NewGuid();

    [Fact]
    public async Task Legal_hold_does_not_call_the_provider()
    {
        var payment = SucceededPayment();
        payment.SetLegalHold(true, Guid.Empty, DateTime.UtcNow);
        var provider = new RefundRecordingProvider();

        var result = await HandleAsync(payment, provider, new FakeUnitOfWork(), 1000);

        Assert.True(result.IsFailure);
        Assert.Equal("Payment.LegalHeld", result.Error.Code);
        Assert.Equal(0, provider.RefundCalls);
    }

    [Fact]
    public async Task Refund_exceeding_principal_does_not_call_the_provider()
    {
        var payment = SucceededPayment();
        payment.RefundPartial(Money.Create(4000, "USD").Value, "earlier refund", Guid.Empty, DateTime.UtcNow);
        var provider = new RefundRecordingProvider();

        var result = await HandleAsync(payment, provider, new FakeUnitOfWork(), 1000);

        Assert.True(result.IsFailure);
        Assert.Equal("SaaSPayment.RefundExceedsPrincipal", result.Error.Code);
        Assert.Equal(0, provider.RefundCalls);
    }

    [Fact]
    public async Task Retry_after_save_failure_reuses_the_same_provider_key()
    {
        var provider = new RefundRecordingProvider();
        var unitOfWork = new ThrowOnFirstSaveUnitOfWork();

        // Cada intento recarga el pago, como un reintento de Wolverine con un scope nuevo.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            HandleAsync(SucceededPayment(), provider, unitOfWork, 1000)
        );
        var retry = await HandleAsync(SucceededPayment(), provider, unitOfWork, 1000);

        Assert.True(retry.IsSuccess);
        Assert.Equal(2, provider.Keys.Count);
        Assert.Equal(provider.Keys[0], provider.Keys[1]);
        Assert.Equal($"refund:{PaymentId:N}:0:1000", provider.Keys[0]);
    }

    [Fact]
    public async Task Client_idempotency_key_scopes_the_provider_key()
    {
        var provider = new RefundRecordingProvider();

        var result = await HandleAsync(SucceededPayment(), provider, new FakeUnitOfWork(), 1000, "k1");

        Assert.True(result.IsSuccess);
        Assert.Equal($"refund:{PaymentId:N}:k1", Assert.Single(provider.Keys));
    }

    private static Task<Result> HandleAsync(
        SaaSPayment payment,
        RefundRecordingProvider provider,
        IUnitOfWork unitOfWork,
        long amountCents,
        string? clientKey = null
    ) =>
        RefundSaaSPaymentHandler.Handle(
            new RefundSaaSPaymentCommand(payment.Id, amountCents, "customer request", Guid.NewGuid(), clientKey),
            new FakeSaaSPaymentRepository { ByIdAnyTenant = payment },
            new FakePaymentAdapterFactory(provider),
            new FakePaymentAuditLogWriter(),
            unitOfWork,
            new FakePaymentAppMetrics(),
            new NoThrottle(),
            new FakeCorrelationContext(),
            NullLogger<SaaSPayment>.Instance,
            CancellationToken.None
        );

    private static SaaSPayment SucceededPayment()
    {
        var nowUtc = DateTime.UtcNow;
        var payment = SaaSPayment
            .Create(
                Guid.NewGuid(),
                IdempotencyKey.Create($"seat-purchase-{Guid.NewGuid():N}").Value,
                Money.Create(4900, "USD").Value,
                SaaSPaymentType.SeatsPurchaseCharge,
                Guid.NewGuid(),
                PaymentProviderCode.Stripe,
                StatementDescriptor.Create("TAXVISION SAAS").Value,
                Guid.Empty,
                nowUtc
            )
            .Value;
        // Mismo Id en cada carga: el pago es la misma fila.
        typeof(SaaSPayment).GetProperty(nameof(SaaSPayment.Id))!.SetValue(payment, PaymentId);
        payment.MarkProcessing(
            ExternalPaymentReference.Create(PaymentProviderCode.Stripe, "pi_paid").Value,
            "processing",
            null,
            Guid.Empty,
            nowUtc
        );
        payment.MarkSucceeded(nowUtc, Guid.Empty);
        return payment;
    }

    private sealed class ThrowOnFirstSaveUnitOfWork : IUnitOfWork
    {
        private int _calls;

        public Task<int> SaveChangesAsync(CancellationToken ct = default) =>
            ++_calls == 1 ? throw new InvalidOperationException("Simulated save failure.") : Task.FromResult(1);
    }

    private sealed class NoThrottle : IPaymentAttemptThrottle
    {
        public Task<bool> IsWebhookThrottledAsync(Guid scopeId, CancellationToken ct = default) =>
            Task.FromResult(false);

        public Task RegisterWebhookAttemptAsync(Guid scopeId, CancellationToken ct = default) => Task.CompletedTask;

        public Task<bool> IsAdminActionThrottledAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.FromResult(false);

        public Task RegisterAdminActionAttemptAsync(Guid tenantId, CancellationToken ct = default) =>
            Task.CompletedTask;
    }
}

/// <summary>Proveedor Stripe falso que solo reembolsa y anota cada llamada con su clave.</summary>
internal sealed class RefundRecordingProvider : RefundRecordingProviderBase;

internal abstract class RefundRecordingProviderBase : IPaymentProvider
{
    public int RefundCalls => Keys.Count;
    public int Calls => Keys.Count;
    public List<string> Keys { get; } = [];

    /// <summary>Devuelve el id del reembolso para la clave recibida.</summary>
    protected virtual string Refund(string idempotencyKey) => $"re_{Keys.Count}";

    public PaymentProviderCode Code => PaymentProviderCode.Stripe;
    public ProviderCapabilities Capabilities =>
        TaxVision.PaymentApp.Infrastructure.Providers.Stripe.StripeCapabilities.Instance;

    public Task<Result<RefundResult>> RefundAsync(
        string providerChargeReference,
        Money amount,
        string reason,
        string idempotencyKey,
        CancellationToken ct
    )
    {
        Keys.Add(idempotencyKey);
        return Task.FromResult(
            Result.Success(new RefundResult(Refund(idempotencyKey), PaymentStatus.Refunded, amount))
        );
    }

    public Task<Result<ProviderCustomerToken>> GetOrCreateCustomerAsync(
        Guid tenantId,
        string email,
        string? name,
        CancellationToken ct
    ) => throw new NotSupportedException();

    public Task<Result<ChargeAuthorizationResult>> AuthorizeChargeAsync(
        ChargeAuthorizationRequest request,
        CancellationToken ct
    ) => throw new NotSupportedException();

    public Task<Result<CaptureResult>> CaptureAsync(
        string providerChargeReference,
        Money amount,
        CancellationToken ct
    ) => throw new NotSupportedException();

    public Task<Result<WebhookVerificationResult>> VerifyWebhookSignatureAsync(
        ProviderWebhookVerificationRequest request,
        CancellationToken ct
    ) => throw new NotSupportedException();

    public Task<Result<WebhookEventPayload>> ParseWebhookEventAsync(
        string rawPayload,
        string eventType,
        CancellationToken ct
    ) => throw new NotSupportedException();

    public Task<Result<ChargeAuthorizationResult>> GetChargeStatusAsync(
        string providerChargeReference,
        CancellationToken ct
    ) => throw new NotSupportedException();

    public Task<Result<ChargeAuthorizationResult>> FinalizeHostedCheckoutAsync(
        string providerChargeReference,
        Money amount,
        CancellationToken ct
    ) => throw new NotSupportedException();

    public Task<Result<SetupIntentInfo>> CreateSetupIntentAsync(ProviderCustomerToken customer, CancellationToken ct) =>
        throw new NotSupportedException();

    public Task<Result<SavedPaymentMethodInfo>> AttachPaymentMethodAsync(
        ProviderCustomerToken customer,
        string paymentMethodReference,
        CancellationToken ct
    ) => throw new NotSupportedException();

    public Task<Result> DetachPaymentMethodAsync(string paymentMethodReference, CancellationToken ct) =>
        throw new NotSupportedException();

    public Task<Result<HostedCheckoutSessionResult>> CreateHostedCheckoutSessionAsync(
        HostedCheckoutSessionRequest request,
        CancellationToken ct
    ) => throw new NotSupportedException();
}
