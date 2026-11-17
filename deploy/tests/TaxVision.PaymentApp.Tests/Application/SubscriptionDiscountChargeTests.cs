using BuildingBlocks.Messaging.PaymentAppIntegrationEvents;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.ValueObjects;
using TaxVision.PaymentApp.Tests.TestDoubles;

namespace TaxVision.PaymentApp.Tests.Application;

/// <summary>
/// Un cobro de la suscripción con su descuento vigente: PaymentApp guarda el descuento y su fuente (Code | Admin) y
/// los publica en el aviso de pago exitoso, que es lo que usa el recibo.
/// </summary>
public sealed class SubscriptionDiscountChargeTests
{
    [Fact]
    public async Task Discount_source_is_stored_and_published()
    {
        var payment = Charged(discountCents: 980, source: "Admin");

        var emitted = await SaaSPaymentEventReplay.DispatchAsync(payment);

        var succeeded = Assert.Single(emitted.OfType<SaaSPaymentSucceededIntegrationEvent>());
        Assert.Equal(3_920, succeeded.AmountPaidCents);
        Assert.Equal(980, succeeded.DiscountAmountCents);
        Assert.Equal("Admin", succeeded.DiscountSource);
    }

    [Fact]
    public async Task A_charge_without_discount_publishes_none()
    {
        var payment = Charged(discountCents: null, source: null);

        var emitted = await SaaSPaymentEventReplay.DispatchAsync(payment);

        var succeeded = Assert.Single(emitted.OfType<SaaSPaymentSucceededIntegrationEvent>());
        Assert.Null(succeeded.DiscountAmountCents);
        Assert.Null(succeeded.DiscountSource);
    }

    private static SaaSPayment Charged(long? discountCents, string? source)
    {
        var payment = SaaSPayment
            .Create(
                Guid.Parse("55555555-5555-5555-5555-555555555555"),
                IdempotencyKey.Create($"discounted-{Guid.NewGuid():N}").Value,
                Money.Create(3_920, "USD").Value,
                SaaSPaymentType.WalletTopUp,
                Guid.Parse("66666666-6666-6666-6666-666666666666"),
                PaymentProviderCode.Stripe,
                StatementDescriptor.Create("TAXVISION SAAS").Value,
                Guid.Empty,
                DateTime.UtcNow,
                discountAmountCents: discountCents,
                discountSource: source
            )
            .Value;
        payment.MarkProcessing(
            ExternalPaymentReference.Create(PaymentProviderCode.Stripe, "pi_discounted").Value,
            "processing",
            providerResponseBody: null,
            Guid.Empty,
            DateTime.UtcNow
        );
        Assert.True(payment.MarkSucceeded(DateTime.UtcNow, Guid.Empty).IsSuccess);
        return payment;
    }
}
