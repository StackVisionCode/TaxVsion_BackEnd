using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Application.HostedCheckout.Commands;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.ValueObjects;
using TaxVision.PaymentApp.Tests.TestDoubles;

namespace TaxVision.PaymentApp.Tests.Application;

/// <summary>Cancel/expire best-effort de una Checkout Session hosteada: idempotente y nunca bloquea el flujo
/// local de Subscription (el intent allá ya está Cancelled).</summary>
public sealed class CancelHostedCheckoutSessionHandlerTests
{
    [Fact]
    public async Task Fails_with_NotFound_when_the_session_id_is_unknown_and_no_code_is_provided()
    {
        var payments = new FakeSaaSPaymentRepository { BySessionId = null };
        var canceller = new FakeCanceller();

        var result = await CancelHostedCheckoutSessionHandler.Handle(
            new CancelHostedCheckoutSessionCommand("cs_unknown", ProviderCode: null),
            payments,
            canceller,
            NullLogger<SaaSPayment>.Instance,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("SaaSPayment.NotFound", result.Error.Code);
        Assert.Null(canceller.LastCode);
    }

    [Fact]
    public async Task Dispatches_to_the_stripe_canceller_when_resolved_payment_is_stripe()
    {
        var payment = PaymentWithSession(PaymentProviderCode.Stripe, PaymentStatus.Processing);
        var payments = new FakeSaaSPaymentRepository { BySessionId = payment };
        var canceller = new FakeCanceller(alreadyExpired: false);

        var result = await CancelHostedCheckoutSessionHandler.Handle(
            new CancelHostedCheckoutSessionCommand("cs_existing", ProviderCode: null),
            payments,
            canceller,
            NullLogger<SaaSPayment>.Instance,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.AlreadyExpired);
        Assert.Equal(PaymentProviderCode.Stripe, canceller.LastCode);
        Assert.Equal("cs_existing", canceller.LastSessionId);
    }

    [Fact]
    public async Task Dispatches_to_the_paypal_canceller_when_resolved_payment_is_paypal()
    {
        var payment = PaymentWithSession(PaymentProviderCode.PayPal, PaymentStatus.Processing);
        var payments = new FakeSaaSPaymentRepository { BySessionId = payment };
        var canceller = new FakeCanceller(alreadyExpired: false);

        var result = await CancelHostedCheckoutSessionHandler.Handle(
            new CancelHostedCheckoutSessionCommand("ORDER-123", ProviderCode: null),
            payments,
            canceller,
            NullLogger<SaaSPayment>.Instance,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentProviderCode.PayPal, canceller.LastCode);
    }

    [Fact]
    public async Task Short_circuits_when_the_local_payment_is_already_terminal()
    {
        var payment = PaymentWithSession(PaymentProviderCode.Stripe, PaymentStatus.Succeeded);
        var payments = new FakeSaaSPaymentRepository { BySessionId = payment };
        var canceller = new FakeCanceller();

        var result = await CancelHostedCheckoutSessionHandler.Handle(
            new CancelHostedCheckoutSessionCommand("cs_existing", ProviderCode: null),
            payments,
            canceller,
            NullLogger<SaaSPayment>.Instance,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.AlreadyExpired);
        Assert.Null(canceller.LastCode);
    }

    [Fact]
    public async Task Honors_the_explicit_provider_code_without_a_lookup()
    {
        // Si el caller trae el code, no hace falta el fetch — útil para onboarding pre-tenant donde la fila
        // puede estar con TenantId=Guid.Empty.
        var payments = new FakeSaaSPaymentRepository { BySessionId = null };
        var canceller = new FakeCanceller(alreadyExpired: true);

        var result = await CancelHostedCheckoutSessionHandler.Handle(
            new CancelHostedCheckoutSessionCommand("cs_explicit", PaymentProviderCode.Stripe),
            payments,
            canceller,
            NullLogger<SaaSPayment>.Instance,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.AlreadyExpired);
        Assert.Equal(PaymentProviderCode.Stripe, canceller.LastCode);
    }

    [Fact]
    public async Task Rejects_an_empty_session_id()
    {
        var result = await CancelHostedCheckoutSessionHandler.Handle(
            new CancelHostedCheckoutSessionCommand(" ", ProviderCode: PaymentProviderCode.Stripe),
            new FakeSaaSPaymentRepository(),
            new FakeCanceller(),
            NullLogger<SaaSPayment>.Instance,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("SaaSPayment.InvalidCheckoutSession", result.Error.Code);
    }

    private static SaaSPayment PaymentWithSession(PaymentProviderCode providerCode, PaymentStatus targetStatus)
    {
        var tenantId = Guid.NewGuid();
        var payment = SaaSPayment
            .Create(
                tenantId,
                IdempotencyKey.Create($"cancel-test-{Guid.NewGuid():N}").Value,
                Money.Create(3000, "USD").Value,
                SaaSPaymentType.SeatsPurchaseCharge,
                Guid.NewGuid(),
                providerCode,
                StatementDescriptor.Create("TAXVISION").Value,
                actorUserId: Guid.Empty,
                DateTime.UtcNow
            )
            .Value;

        payment.RecordHostedCheckoutSession(
            "cs_existing",
            ExternalPaymentReference.Create(providerCode, "pi_existing").Value,
            "https://checkout.example.com/existing",
            DateTime.UtcNow
        );

        if (targetStatus == PaymentStatus.Succeeded)
            payment.MarkSucceeded(DateTime.UtcNow, Guid.Empty);

        return payment;
    }

    private sealed class FakeCanceller(bool alreadyExpired = false) : IHostedCheckoutSessionCanceller
    {
        public PaymentProviderCode? LastCode { get; private set; }
        public string? LastSessionId { get; private set; }

        public Task<Result<CancelHostedCheckoutResult>> CancelAsync(
            PaymentProviderCode providerCode,
            string providerSessionId,
            CancellationToken ct = default
        )
        {
            LastCode = providerCode;
            LastSessionId = providerSessionId;
            return Task.FromResult(Result.Success(new CancelHostedCheckoutResult(alreadyExpired)));
        }
    }
}
