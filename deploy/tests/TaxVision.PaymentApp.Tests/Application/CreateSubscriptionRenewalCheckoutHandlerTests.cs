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
            "https://app.taxoffix.io/success",
            "https://app.taxoffix.io/cancel",
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
            new FakePurchaseIntentAmountVerifier(),
            NullLogger<SaaSPayment>.Instance,
            TestPlatform.Settings,
            TestPlatform.ReturnUrls,
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

    [Fact]
    public async Task A_discounted_renewal_stores_the_discount_and_its_source()
    {
        var payments = new FakeSaaSPaymentRepository();

        var result = await CreateSubscriptionRenewalCheckoutHandler.Handle(
            Command(Guid.NewGuid(), Guid.NewGuid(), amountCents: 3_920) with
            {
                DiscountAmountCents = 980,
                DiscountSource = "Admin",
                DiscountLabel = "Discount · 20% off",
            },
            payments,
            new FakePaymentAdapterFactory(new FakePaymentProvider()),
            new FakePaymentAuditLogWriter(),
            new FakeUnitOfWork(),
            new FakePaymentAppMetrics(),
            new FakeCorrelationContext(),
            new FakePurchaseIntentAmountVerifier(),
            NullLogger<SaaSPayment>.Instance,
            TestPlatform.Settings,
            TestPlatform.ReturnUrls,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Equal(3_920, payments.Added!.Amount.AmountCents);
        Assert.Equal(980, payments.Added.DiscountAmountCents);
        Assert.Equal("Admin", payments.Added.DiscountSource);
        Assert.Equal("Discount · 20% off", payments.Added.DiscountLabel);
    }

    // "Pay what's due": un checkout con el plan y lo vencido guarda una línea por cosa para el recibo.
    [Fact]
    public async Task A_due_payment_keeps_one_line_per_item()
    {
        var payments = new FakeSaaSPaymentRepository();
        var seatId = Guid.NewGuid();

        var result = await CreateSubscriptionRenewalCheckoutHandler.Handle(
            Command(Guid.NewGuid(), Guid.NewGuid(), amountCents: 4_400) with
            {
                Lines =
                [
                    new BuildingBlocks.Messaging.PaymentAppIntegrationEvents.SaaSChargeLine(
                        "Pro plan",
                        1,
                        2_900,
                        "Plan",
                        Guid.NewGuid()
                    ),
                    new BuildingBlocks.Messaging.PaymentAppIntegrationEvents.SaaSChargeLine(
                        "Standard seat",
                        1,
                        1_500,
                        "Seat",
                        seatId
                    ),
                ],
            },
            payments,
            new FakePaymentAdapterFactory(new FakePaymentProvider()),
            new FakePaymentAuditLogWriter(),
            new FakeUnitOfWork(),
            new FakePaymentAppMetrics(),
            new FakeCorrelationContext(),
            new FakePurchaseIntentAmountVerifier(),
            NullLogger<SaaSPayment>.Instance,
            TestPlatform.Settings,
            TestPlatform.ReturnUrls,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Code : null);
        Assert.Equal(2, payments.Added!.Lines.Count);
        Assert.Contains(payments.Added.Lines, line => line.TargetId == seatId);
    }
}
