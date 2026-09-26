using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.PaymentApp.Application.Abstractions;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Application.SubscriptionRenewalCheckouts.Commands;
using TaxVision.PaymentApp.Domain.Audit;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.ValueObjects;
using TaxVision.PaymentApp.Tests.TestDoubles;

namespace TaxVision.PaymentApp.Tests.Application;

/// <summary>El checkout hosteado de una renovación self-service crea un <see cref="SaaSPayment"/> de tipo
/// <see cref="SaaSPaymentType.SubscriptionRenewalCheckout"/> con el <c>RenewalIntentId</c> en
/// <c>TargetAggregateId</c> (la clave que usa el webhook para reactivar), por el monto que Subscription resolvió,
/// y devuelve la URL. Espejo de <c>CreateSeatsCheckoutHandlerTests</c>.</summary>
public sealed class CreateSubscriptionRenewalCheckoutHandlerTests
{
    private static CreateSubscriptionRenewalCheckoutCommand Command(Guid tenantId, Guid intentId, long amountCents) =>
        new(
            tenantId,
            intentId,
            amountCents,
            "USD",
            "owner@example.com",
            "https://app.example.com/success",
            "https://app.example.com/cancel",
            $"subscription-renewal-checkout-{intentId:N}"
        );

    [Fact]
    public async Task Creates_a_renewal_charge_correlated_to_the_intent_and_returns_the_url()
    {
        var tenantId = Guid.NewGuid();
        var intentId = Guid.NewGuid();
        var payments = new FakeSaaSPaymentRepository();
        var provider = new FakePaymentProvider();

        var result = await CreateSubscriptionRenewalCheckoutHandler.Handle(
            Command(tenantId, intentId, amountCents: 4900),
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
        Assert.Equal(SaaSPaymentType.SubscriptionRenewalCheckout, payments.Added!.Type);
        Assert.Equal(intentId, payments.Added.TargetAggregateId);
        Assert.Equal(tenantId, payments.Added.TenantId);
        Assert.Equal(4900, payments.Added.Amount.AmountCents);
    }
}
