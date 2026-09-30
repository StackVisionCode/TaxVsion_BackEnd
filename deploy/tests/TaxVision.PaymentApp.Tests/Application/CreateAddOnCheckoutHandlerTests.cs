using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Application.AddOnCheckouts.Commands;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.ValueObjects;
using TaxVision.PaymentApp.Tests.TestDoubles;

namespace TaxVision.PaymentApp.Tests.Application;

/// <summary>
/// El checkout hosteado de la compra de un add-on: crea un <see cref="SaaSPayment"/> de tipo
/// <see cref="SaaSPaymentType.AddOnPurchaseCharge"/> con el <c>AddOnPurchaseIntentId</c> en
/// <c>TargetAggregateId</c> — la clave de correlación con la que el webhook activa el add-on en Subscription.
/// </summary>
public sealed class CreateAddOnCheckoutHandlerTests
{
    private static CreateAddOnCheckoutCommand Command(Guid tenantId, Guid intentId) =>
        new(
            tenantId,
            intentId,
            AmountCents: 2900,
            "USD",
            "buyer@example.com",
            "https://app.example.com/success",
            "https://app.example.com/cancel",
            $"addon-checkout-{intentId:N}"
        );

    [Fact]
    public async Task Creates_an_add_on_charge_correlated_to_the_intent_and_returns_the_url()
    {
        var tenantId = Guid.NewGuid();
        var intentId = Guid.NewGuid();
        var payments = new FakeSaaSPaymentRepository();
        var provider = new FakePaymentProvider();

        var result = await CreateAddOnCheckoutHandler.Handle(
            Command(tenantId, intentId),
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
        Assert.Equal(SaaSPaymentType.AddOnPurchaseCharge, payments.Added!.Type);
        Assert.Equal(intentId, payments.Added.TargetAggregateId);
        Assert.Equal(tenantId, payments.Added.TenantId);
        Assert.Equal(2900, payments.Added.Amount.AmountCents);
        Assert.Equal(intentId.ToString("N"), provider.LastRequest!.Metadata["addOnPurchaseIntentId"]);
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

        var command = Command(Guid.NewGuid(), Guid.NewGuid()) with
        {
            Provider = PaymentProviderCode.Manual,
            Method = PaymentMethodKind.Manual,
        };

        var result = await CreateAddOnCheckoutHandler.Handle(
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
