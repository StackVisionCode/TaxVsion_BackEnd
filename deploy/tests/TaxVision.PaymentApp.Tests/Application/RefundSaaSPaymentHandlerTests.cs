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
        payment.SetLegalHold(true, Guid.NewGuid(), DateTime.UtcNow);
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
    public async Task Transient_provider_failure_keeps_the_request_and_the_retry_reuses_the_key()
    {
        var payment = SucceededPayment();
        var provider = new RefundRecordingProvider { FailWith = new Error("Stripe.Refund.Unavailable", "503") };
        var actor = Guid.NewGuid();

        var first = await HandleAsync(payment, provider, new FakeUnitOfWork(), 1000, actor: actor);
        provider.FailWith = null;
        var retry = await HandleAsync(payment, provider, new FakeUnitOfWork(), 1000, actor: actor);

        Assert.Equal("Stripe.Refund.Unavailable", first.Error.Code);
        Assert.True(retry.IsSuccess);
        Assert.Equal(2, provider.Keys.Count);
        Assert.Equal(provider.Keys[0], provider.Keys[1]);
        var request = Assert.Single(payment.RefundRequests);
        Assert.Equal($"refund:{PaymentId:N}:{request.Id:N}", provider.Keys[0]);
        Assert.Single(payment.Refunds);
    }

    [Fact]
    public async Task Identical_request_after_the_window_is_a_new_refund()
    {
        var payment = SucceededPayment();
        var provider = new HonoringProvider();
        var actor = Guid.NewGuid();

        await HandleAsync(
            payment,
            provider,
            new FakeUnitOfWork(),
            1000,
            actor: actor,
            window: TimeSpan.FromMilliseconds(1)
        );
        await Task.Delay(20);
        var second = await HandleAsync(
            payment,
            provider,
            new FakeUnitOfWork(),
            1000,
            actor: actor,
            window: TimeSpan.FromMilliseconds(1)
        );

        Assert.True(second.IsSuccess);
        Assert.Equal(2, provider.EffectiveRefunds);
        Assert.Equal(2000, payment.RefundedAmountCents);
    }

    [Fact]
    public async Task Another_request_while_one_is_pending_does_not_call_the_provider()
    {
        var payment = SucceededPayment();
        var provider = new RefundRecordingProvider { FailWith = new Error("Stripe.Refund.Unavailable", "503") };

        await HandleAsync(payment, provider, new FakeUnitOfWork(), 1000);
        provider.FailWith = null;
        var other = await HandleAsync(payment, provider, new FakeUnitOfWork(), 500);

        Assert.Equal("SaaSPayment.RefundInProgress", other.Error.Code);
        Assert.Equal(1, provider.RefundCalls);
    }

    [Fact]
    public async Task Definitive_rejection_is_audited_and_returns_the_provider_error()
    {
        var payment = SucceededPayment();
        var provider = new RefundRecordingProvider { FailWith = new Error("Stripe.Refund.Failed", "disputed") };
        var audit = new RecordingAudit();

        var result = await HandleAsync(payment, provider, new FakeUnitOfWork(), 1000, audit: audit);

        Assert.Equal("Stripe.Refund.Failed", result.Error.Code);
        Assert.Equal(RefundRequestStatus.Rejected, Assert.Single(payment.RefundRequests).Status);
        Assert.Contains(
            audit.Entries,
            e => e.Action == TaxVision.PaymentApp.Domain.Audit.PaymentAuditAction.SaaSPaymentRefundRejected
        );
        Assert.Empty(payment.Refunds);
        Assert.Null(payment.ReviewRequiredAtUtc);
    }

    [Fact]
    public async Task Provider_without_idempotency_keys_never_resends_an_unknown_outcome()
    {
        var payment = SucceededPayment();
        var provider = new RefundRecordingProvider
        {
            FailWith = new Error("Intellipay.Refund.Unavailable", "timeout"),
            Capabilities = TaxVision.PaymentApp.Infrastructure.Providers.Intellipay.IntellipayCapabilities.Instance,
        };
        var actor = Guid.NewGuid();

        var first = await HandleAsync(payment, provider, new FakeUnitOfWork(), 1000, actor: actor);
        provider.FailWith = null;
        var again = await HandleAsync(payment, provider, new FakeUnitOfWork(), 1000, actor: actor);

        Assert.Equal("SaaSPayment.RefundNeedsReconciliation", first.Error.Code);
        Assert.Equal("SaaSPayment.RefundNeedsReconciliation", again.Error.Code);
        Assert.Equal(1, provider.RefundCalls);
        Assert.Equal(SaaSPayment.ReviewReasonRefundNeedsReconciliation, payment.ReviewReasonCode);
    }

    [Fact]
    public async Task Identical_request_without_header_after_commit_does_not_refund_twice()
    {
        // El mismo pago (ya guardado) recibe la misma petición dos veces, sin Idempotency-Key.
        var payment = SucceededPayment();
        var provider = new HonoringProvider();
        var repository = new FakeSaaSPaymentRepository { ByIdAnyTenant = payment };
        var actor = Guid.NewGuid();

        var first = await HandleAsync(
            payment,
            provider,
            new FakeUnitOfWork(),
            1000,
            actor: actor,
            repository: repository
        );
        var second = await HandleAsync(
            payment,
            provider,
            new FakeUnitOfWork(),
            1000,
            actor: actor,
            repository: repository
        );

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        Assert.Equal(1, provider.EffectiveRefunds);
        Assert.Single(payment.Refunds);
        Assert.Equal(1000, payment.RefundedAmountCents);
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
        RefundRecordingProviderBase provider,
        IUnitOfWork unitOfWork,
        long amountCents,
        string? clientKey = null,
        Guid? actor = null,
        FakeSaaSPaymentRepository? repository = null,
        IPaymentAuditLogWriter? audit = null,
        TimeSpan? window = null
    )
    {
        var payments = repository ?? new FakeSaaSPaymentRepository { ByIdAnyTenant = payment };
        var adapters = new FakePaymentAdapterFactory(provider);
        return RefundSaaSPaymentHandler.Handle(
            new RefundSaaSPaymentCommand(
                payment.Id,
                amountCents,
                "customer request",
                actor ?? Guid.NewGuid(),
                clientKey
            ),
            TestRefundExecution.Bus(() => (payments, unitOfWork), adapters, audit),
            adapters,
            new NoAdminThrottle(),
            TestRefundExecution.RefundOptions(window),
            NullLogger<SaaSPayment>.Instance,
            CancellationToken.None
        );
    }

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

    private sealed class RecordingAudit : IPaymentAuditLogWriter
    {
        public List<TaxVision.PaymentApp.Domain.Audit.PaymentAuditEntry> Entries { get; } = [];

        public Task AppendAsync(
            TaxVision.PaymentApp.Domain.Audit.PaymentAuditEntry entry,
            CancellationToken ct = default
        )
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }
}

/// <summary>Proveedor Stripe falso que solo reembolsa y anota cada llamada con su clave.</summary>
internal sealed class RefundRecordingProvider : RefundRecordingProviderBase;

/// <summary>Honra la idempotencia como Stripe: la misma clave devuelve el mismo reembolso y no crea otro.</summary>
internal sealed class HonoringProvider : RefundRecordingProviderBase
{
    private readonly Dictionary<string, string> _refundsByKey = [];
    public int EffectiveRefunds => _refundsByKey.Count;

    protected override string Refund(string idempotencyKey)
    {
        if (!_refundsByKey.TryGetValue(idempotencyKey, out var refundId))
            _refundsByKey[idempotencyKey] = refundId = $"re_{_refundsByKey.Count + 1}";
        return refundId;
    }
}

internal abstract class RefundRecordingProviderBase : IPaymentProvider
{
    public int RefundCalls => Keys.Count;
    public int Calls => Keys.Count;
    public List<string> Keys { get; } = [];

    /// <summary>Devuelve el id del reembolso para la clave recibida.</summary>
    protected virtual string Refund(string idempotencyKey) => $"re_{Keys.Count}";

    public PaymentProviderCode Code => PaymentProviderCode.Stripe;
    public ProviderCapabilities Capabilities { get; init; } =
        TaxVision.PaymentApp.Infrastructure.Providers.Stripe.StripeCapabilities.Instance;

    /// <summary>Si tiene valor, el proveedor responde este error en vez de reembolsar.</summary>
    public Error? FailWith { get; set; }

    /// <summary>Corre dentro de la llamada, como un cambio concurrente mientras el proveedor responde.</summary>
    public Action? DuringRefund { get; set; }

    public Task<Result<RefundResult>> RefundAsync(
        string providerChargeReference,
        Money amount,
        string reason,
        string idempotencyKey,
        CancellationToken ct
    )
    {
        Keys.Add(idempotencyKey);
        DuringRefund?.Invoke();
        if (FailWith is not null)
            return Task.FromResult(Result.Failure<RefundResult>(FailWith));
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

    public Task<Result<ChargeLookup>> FindChargeAsync(
        Guid saaSPaymentId,
        DateTime createdAfterUtc,
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
