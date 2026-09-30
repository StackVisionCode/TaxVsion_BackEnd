using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Application.SeatsCheckouts.Commands;
using TaxVision.PaymentApp.Domain.Audit;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.ValueObjects;
using TaxVision.PaymentApp.Tests.TestDoubles;

namespace TaxVision.PaymentApp.Tests.Application;

/// <summary>
/// El checkout hosteado de una compra de asientos: crea un <see cref="SaaSPayment"/> de tipo
/// <see cref="SaaSPaymentType.SeatsPurchaseCharge"/> con el <c>SeatPurchaseIntentId</c> en
/// <c>TargetAggregateId</c> (la clave de correlación que usa el webhook para aprovisionar), por el monto que
/// Subscription ya resolvió, y devuelve la URL de checkout.
/// </summary>
public sealed class CreateSeatsCheckoutHandlerTests
{
    private static CreateSeatsCheckoutCommand Command(Guid tenantId, Guid intentId, long amountCents) =>
        new(
            tenantId,
            intentId,
            amountCents,
            "USD",
            "buyer@example.com",
            "https://app.example.com/success",
            "https://app.example.com/cancel",
            $"seat-checkout-{intentId:N}"
        );

    [Fact]
    public async Task Creates_a_seats_purchase_charge_correlated_to_the_intent_and_returns_the_url()
    {
        var tenantId = Guid.NewGuid();
        var intentId = Guid.NewGuid();
        var payments = new FakeSaaSPaymentRepository();
        var provider = new FakePaymentProvider();

        var result = await CreateSeatsCheckoutHandler.Handle(
            Command(tenantId, intentId, amountCents: 3000),
            payments,
            new FakePaymentAdapterFactory(provider),
            new FakePaymentAuditLogWriter(),
            new FakeUnitOfWork(),
            new FakePaymentAppMetrics(),
            new FakeCorrelationContext(),
            NullLogger<SaaSPayment>.Instance,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal("https://checkout.example.com/session", result.Value.CheckoutUrl);
        Assert.NotNull(payments.Added);
        Assert.Equal(SaaSPaymentType.SeatsPurchaseCharge, payments.Added!.Type);
        Assert.Equal(intentId, payments.Added.TargetAggregateId);
        Assert.Equal(tenantId, payments.Added.TenantId);
        Assert.Equal(3000, payments.Added.Amount.AmountCents);
        Assert.Equal("USD", payments.Added.Amount.Currency);
        Assert.Equal(3000, provider.LastRequest!.Amount.AmountCents);
    }

    // A diferencia del onboarding, una compra de asientos NO reintenta en sitio: el mismo key devuelve la
    // sesión que ya tiene, sin volver a pedirle una al proveedor.
    [Fact]
    public async Task Replays_the_existing_session_without_calling_the_provider_again()
    {
        var intentId = Guid.NewGuid();
        var existing = PaymentWithSession(Guid.NewGuid(), intentId);
        var payments = new FakeSaaSPaymentRepository(existing);
        var provider = new FakePaymentProvider();

        var result = await CreateSeatsCheckoutHandler.Handle(
            Command(existing.TenantId, intentId, amountCents: 3000),
            payments,
            new FakePaymentAdapterFactory(provider),
            new FakePaymentAuditLogWriter(),
            new FakeUnitOfWork(),
            new FakePaymentAppMetrics(),
            new FakeCorrelationContext(),
            NullLogger<SaaSPayment>.Instance,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(existing.Id, result.Value.PaymentId);
        Assert.Equal("https://checkout.example.com/existing", result.Value.CheckoutUrl);
        Assert.Null(provider.LastRequest);
        Assert.Null(payments.Added);
    }

    [Fact]
    public async Task Rejects_a_previous_payment_that_never_got_a_session()
    {
        var tenantId = Guid.NewGuid();
        var intentId = Guid.NewGuid();
        var existing = SaaSPayment
            .Create(
                tenantId,
                IdempotencyKey.Create($"seat-checkout-{intentId:N}").Value,
                Money.Create(3000, "USD").Value,
                SaaSPaymentType.SeatsPurchaseCharge,
                intentId,
                PaymentProviderCode.Stripe,
                StatementDescriptor.Create("TAXVISION SEATS").Value,
                actorUserId: Guid.Empty,
                DateTime.UtcNow
            )
            .Value;
        var provider = new FakePaymentProvider();

        var result = await CreateSeatsCheckoutHandler.Handle(
            Command(tenantId, intentId, amountCents: 3000),
            new FakeSaaSPaymentRepository(existing),
            new FakePaymentAdapterFactory(provider),
            new FakePaymentAuditLogWriter(),
            new FakeUnitOfWork(),
            new FakePaymentAppMetrics(),
            new FakeCorrelationContext(),
            NullLogger<SaaSPayment>.Instance,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Seats.Checkout.NotReplayable", result.Error.Code);
        Assert.Null(provider.LastRequest);
    }

    private static SaaSPayment PaymentWithSession(Guid tenantId, Guid intentId)
    {
        var payment = SaaSPayment
            .Create(
                tenantId,
                IdempotencyKey.Create($"seat-checkout-{intentId:N}").Value,
                Money.Create(3000, "USD").Value,
                SaaSPaymentType.SeatsPurchaseCharge,
                intentId,
                PaymentProviderCode.Stripe,
                StatementDescriptor.Create("TAXVISION SEATS").Value,
                actorUserId: Guid.Empty,
                DateTime.UtcNow
            )
            .Value;

        payment.RecordHostedCheckoutSession(
            "cs_existing",
            ExternalPaymentReference.Create(PaymentProviderCode.Stripe, "pi_existing").Value,
            "https://checkout.example.com/existing",
            DateTime.UtcNow
        );

        return payment;
    }

    [Fact]
    public async Task Fails_when_the_provider_does_not_support_hosted_checkout()
    {
        var payments = new FakeSaaSPaymentRepository();
        var provider = new FakePaymentProvider(
            PaymentProviderCode.Manual,
            new HashSet<PaymentMethodKind> { PaymentMethodKind.Manual },
            supportsHostedCheckout: false
        );

        var command = Command(Guid.NewGuid(), Guid.NewGuid(), 3000) with
        {
            Provider = PaymentProviderCode.Manual,
            Method = PaymentMethodKind.Manual,
        };

        var result = await CreateSeatsCheckoutHandler.Handle(
            command,
            payments,
            new FakePaymentAdapterFactory(provider),
            new FakePaymentAuditLogWriter(),
            new FakeUnitOfWork(),
            new FakePaymentAppMetrics(),
            new FakeCorrelationContext(),
            NullLogger<SaaSPayment>.Instance,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("PaymentMethod.UnsupportedForCheckout", result.Error.Code);
        Assert.Null(payments.Added);
        Assert.Null(provider.LastRequest);
    }
}
