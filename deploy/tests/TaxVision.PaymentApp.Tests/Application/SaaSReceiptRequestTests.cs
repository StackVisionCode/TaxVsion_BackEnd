using BuildingBlocks.Messaging.DocumentsIntegrationEvents;
using BuildingBlocks.Messaging.PaymentAppIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.PaymentApp.Application.SaaSPayments.Common;
using TaxVision.PaymentApp.Application.SaaSPayments.IntegrationEvents;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.ValueObjects;
using TaxVision.PaymentApp.Tests.TestDoubles;

namespace TaxVision.PaymentApp.Tests.Application;

/// <summary>
/// Todo cobro confirmado de un tenant real pide su recibo, y cuando el PDF está listo queda colgado del
/// pago — que es de donde el historial del Account lo ofrece.
/// </summary>
public sealed class SaaSReceiptRequestTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    [Fact]
    public async Task A_confirmed_charge_asks_for_its_receipt_with_the_office_name()
    {
        var payment = SucceededPayment(SaaSPaymentType.SeatsPurchaseCharge);
        var bus = new CapturingMessageBus();

        await SaaSPaymentResultPublisher.PublishAsync(
            payment,
            bus,
            "corr",
            CancellationToken.None,
            new FakeTenantRegistry("Acme Tax")
        );

        var requested = Assert.Single(bus.Published.OfType<SaaSPaymentSucceededIntegrationEvent>());
        Assert.Equal(payment.Id, requested.SaaSPaymentId);
        Assert.Equal("Acme Tax", requested.OfficeName);
        Assert.Equal("SeatsPurchaseCharge", requested.PaymentType);
        Assert.Equal(1500, requested.AmountPaidCents);
    }

    // Nunca la referencia completa del proveedor: solo lo justo para reconocerla en el extracto.
    [Fact]
    public async Task The_provider_reference_travels_masked()
    {
        var payment = SucceededPayment(SaaSPaymentType.SubscriptionRenewal);
        var bus = new CapturingMessageBus();

        await SaaSPaymentResultPublisher.PublishAsync(
            payment,
            bus,
            "corr",
            CancellationToken.None,
            new FakeTenantRegistry("Acme Tax")
        );

        var requested = Assert.Single(bus.Published.OfType<SaaSPaymentSucceededIntegrationEvent>());
        Assert.Equal("st_1", requested.ProviderReferenceMask);
    }

    // El pago del onboarding nace sin tenant y ya tiene su propio recibo, pedido por Auth.
    [Fact]
    public async Task An_onboarding_payment_does_not_ask_for_this_receipt()
    {
        var payment = SaaSPayment
            .CreateForOnboarding(
                Guid.NewGuid(),
                IdempotencyKey.Create("onb-x").Value,
                Money.Create(4900, "USD").Value,
                Guid.NewGuid(),
                PaymentProviderCode.Stripe,
                StatementDescriptor.Create("TAXVISION SAAS").Value,
                DateTime.UtcNow
            )
            .Value;
        MarkPaid(payment);
        var bus = new CapturingMessageBus();

        await SaaSPaymentResultPublisher.PublishAsync(payment, bus, "corr", CancellationToken.None);

        Assert.Empty(bus.Published.OfType<SaaSPaymentSucceededIntegrationEvent>());
    }

    [Fact]
    public async Task The_generated_receipt_is_attached_to_its_payment()
    {
        var payment = SucceededPayment(SaaSPaymentType.AddOnPurchaseCharge);
        var fileId = Guid.NewGuid();
        var payments = new FakeSaaSPaymentRepository { ById = payment };
        var unitOfWork = new FakeUnitOfWork();

        await SaaSReceiptReadyConsumer.Handle(
            new SaaSReceiptReadyIntegrationEvent
            {
                TenantId = TenantId,
                SaaSPaymentId = payment.Id,
                ReceiptFileId = fileId,
            },
            payments,
            unitOfWork,
            new FakeCorrelationContext(),
            NullLogger<SaaSPayment>.Instance,
            CancellationToken.None
        );

        Assert.Equal(fileId, payment.ReceiptFileId);
        Assert.Equal(1, unitOfWork.SaveCalls);
    }

    /// <summary>
    /// El pedido de recibo solo sale en el instante del cobro, así que un tropiezo en la cadena dejaba al
    /// tenant sin recibo para siempre. El backfill lo reenvía — y **solo** eso: reaprovisionar asientos o
    /// add-ons ya entregados sería mucho peor que no tener el PDF.
    /// </summary>
    [Fact]
    public async Task Re_requesting_a_receipt_never_re_provisions_what_was_already_bought()
    {
        var payment = SucceededPayment(SaaSPaymentType.SeatsPurchaseCharge);
        var bus = new CapturingMessageBus();

        await SaaSPaymentResultPublisher.PublishReceiptRequestAsync(
            payment,
            bus,
            "corr",
            new FakeTenantRegistry("Acme Tax"),
            CancellationToken.None
        );

        Assert.Single(bus.Published.OfType<SaaSPaymentSucceededIntegrationEvent>());
        Assert.Empty(bus.Published.OfType<SeatsCheckoutPaidIntegrationEvent>());
    }

    /// <summary>
    /// El recibo tiene que poder decir "3 × $10.53", no solo el total. El desglose viaja en el mismo evento
    /// que ya pide el recibo.
    /// </summary>
    [Fact]
    public async Task The_receipt_request_carries_how_many_units_and_at_what_price()
    {
        var payment = SucceededPayment(SaaSPaymentType.SeatsPurchaseCharge, quantity: 3, unitAmountCents: 500);
        var bus = new CapturingMessageBus();

        await SaaSPaymentResultPublisher.PublishReceiptRequestAsync(
            payment,
            bus,
            "corr",
            new FakeTenantRegistry("Acme Tax"),
            CancellationToken.None
        );

        var requested = Assert.Single(bus.Published.OfType<SaaSPaymentSucceededIntegrationEvent>());
        Assert.Equal(3, requested.Quantity);
        Assert.Equal(500, requested.UnitAmountCents);
        Assert.Equal(1500, requested.AmountPaidCents);
    }

    // Un cobro sin unidades que contar viaja sin desglose y el recibo muestra solo el total.
    [Fact]
    public async Task A_charge_without_units_travels_without_a_breakdown()
    {
        var payment = SucceededPayment(SaaSPaymentType.PlanChangeCharge);
        var bus = new CapturingMessageBus();

        await SaaSPaymentResultPublisher.PublishReceiptRequestAsync(
            payment,
            bus,
            "corr",
            new FakeTenantRegistry("Acme Tax"),
            CancellationToken.None
        );

        var requested = Assert.Single(bus.Published.OfType<SaaSPaymentSucceededIntegrationEvent>());
        Assert.Null(requested.Quantity);
        Assert.Null(requested.UnitAmountCents);
    }

    private static SaaSPayment SucceededPayment(
        SaaSPaymentType type,
        int? quantity = null,
        long? unitAmountCents = null
    )
    {
        var payment = SaaSPayment
            .Create(
                TenantId,
                IdempotencyKey.Create($"key-{type}").Value,
                Money.Create(1500, "USD").Value,
                type,
                Guid.NewGuid(),
                PaymentProviderCode.Stripe,
                StatementDescriptor.Create("TAXVISION SAAS").Value,
                Guid.Empty,
                DateTime.UtcNow,
                breakdown: quantity is null || unitAmountCents is null
                    ? null
                    : ChargeBreakdown.Create(quantity.Value, unitAmountCents.Value).Value
            )
            .Value;
        MarkPaid(payment);
        return payment;
    }

    private static void MarkPaid(SaaSPayment payment)
    {
        payment.MarkProcessing(
            ExternalPaymentReference.Create(PaymentProviderCode.Stripe, "pi_test_1").Value,
            "processing",
            providerResponseBody: null,
            Guid.Empty,
            DateTime.UtcNow
        );
        payment.MarkSucceeded(DateTime.UtcNow, Guid.Empty);
    }
}
