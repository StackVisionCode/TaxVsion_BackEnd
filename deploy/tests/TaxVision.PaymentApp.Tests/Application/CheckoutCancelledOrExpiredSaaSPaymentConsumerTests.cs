using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.PaymentApp.Application.SaaSPayments.IntegrationEvents;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.ValueObjects;
using TaxVision.PaymentApp.Tests.TestDoubles;

namespace TaxVision.PaymentApp.Tests.Application;

/// <summary>Cuando el comprador cierra el checkout voluntariamente, el historial debe decir "Cancelled",
/// no "Failed" (ese es el bug que arregla este consumer).</summary>
public sealed class CheckoutCancelledOrExpiredSaaSPaymentConsumerTests
{
    [Fact]
    public async Task Seat_cancelled_moves_the_payment_from_Processing_to_Cancelled()
    {
        var tenantId = Guid.NewGuid();
        var payment = ProcessingPayment(tenantId);
        var payments = new FakeSaaSPaymentRepository { ById = payment };
        var unitOfWork = new FakeUnitOfWork();

        await CheckoutCancelledOrExpiredSaaSPaymentConsumer.Handle(
            new SeatCheckoutCancelledIntegrationEvent
            {
                TenantId = tenantId,
                SeatPurchaseIntentId = Guid.NewGuid(),
                SaaSPaymentId = payment.Id,
            },
            payments,
            unitOfWork,
            new FakeCorrelationContext(),
            NullLogger<SaaSPayment>.Instance,
            CancellationToken.None
        );

        Assert.Equal(PaymentStatus.Cancelled, payment.Status);
        Assert.Equal(1, unitOfWork.SaveCalls);
    }

    [Fact]
    public async Task Seat_expired_marks_cancelled_too()
    {
        var tenantId = Guid.NewGuid();
        var payment = ProcessingPayment(tenantId);
        var payments = new FakeSaaSPaymentRepository { ById = payment };
        var unitOfWork = new FakeUnitOfWork();

        await CheckoutCancelledOrExpiredSaaSPaymentConsumer.Handle(
            new SeatCheckoutExpiredIntegrationEvent
            {
                TenantId = tenantId,
                SeatPurchaseIntentId = Guid.NewGuid(),
                SaaSPaymentId = payment.Id,
            },
            payments,
            unitOfWork,
            new FakeCorrelationContext(),
            NullLogger<SaaSPayment>.Instance,
            CancellationToken.None
        );

        Assert.Equal(PaymentStatus.Cancelled, payment.Status);
    }

    [Fact]
    public async Task A_redelivery_is_idempotent()
    {
        var tenantId = Guid.NewGuid();
        var payment = ProcessingPayment(tenantId);
        var payments = new FakeSaaSPaymentRepository { ById = payment };
        var unitOfWork = new FakeUnitOfWork();

        var evt = new AddOnCheckoutCancelledIntegrationEvent
        {
            TenantId = tenantId,
            AddOnPurchaseIntentId = Guid.NewGuid(),
            SaaSPaymentId = payment.Id,
        };

        await CheckoutCancelledOrExpiredSaaSPaymentConsumer.Handle(
            evt,
            payments,
            unitOfWork,
            new FakeCorrelationContext(),
            NullLogger<SaaSPayment>.Instance,
            CancellationToken.None
        );
        await CheckoutCancelledOrExpiredSaaSPaymentConsumer.Handle(
            evt,
            payments,
            unitOfWork,
            new FakeCorrelationContext(),
            NullLogger<SaaSPayment>.Instance,
            CancellationToken.None
        );

        Assert.Equal(PaymentStatus.Cancelled, payment.Status);
        // La segunda pasada no vuelve a escribir: ya era Cancelled.
        Assert.Equal(1, unitOfWork.SaveCalls);
    }

    [Fact]
    public async Task A_cancel_arriving_after_the_webhook_failed_still_wins()
    {
        // El bug exacto: el webhook de Stripe (checkout.session.expired) llegó primero y marcó Failed.
        // Luego llega el event de cancelación del user. El historial debe quedar Cancelled.
        var tenantId = Guid.NewGuid();
        var payment = ProcessingPayment(tenantId);
        payment.MarkFailed(
            "Stripe.CheckoutSession.Expired",
            "The checkout session expired.",
            willRetry: false,
            nextRetryAtUtc: null,
            Guid.Empty,
            DateTime.UtcNow
        );
        Assert.Equal(PaymentStatus.Failed, payment.Status);

        var payments = new FakeSaaSPaymentRepository { ById = payment };
        var unitOfWork = new FakeUnitOfWork();

        await CheckoutCancelledOrExpiredSaaSPaymentConsumer.Handle(
            new SubscriptionRenewalCheckoutCancelledIntegrationEvent
            {
                TenantId = tenantId,
                RenewalIntentId = Guid.NewGuid(),
                SaaSPaymentId = payment.Id,
            },
            payments,
            unitOfWork,
            new FakeCorrelationContext(),
            NullLogger<SaaSPayment>.Instance,
            CancellationToken.None
        );

        Assert.Equal(PaymentStatus.Cancelled, payment.Status);
        Assert.Equal(1, unitOfWork.SaveCalls);
    }

    [Fact]
    public async Task A_succeeded_payment_is_left_alone()
    {
        // Si el pago ya cobró antes de que llegara el event, no se re-escribe como Cancelled.
        var tenantId = Guid.NewGuid();
        var payment = ProcessingPayment(tenantId);
        payment.MarkSucceeded(DateTime.UtcNow, Guid.Empty);

        var payments = new FakeSaaSPaymentRepository { ById = payment };
        var unitOfWork = new FakeUnitOfWork();

        await CheckoutCancelledOrExpiredSaaSPaymentConsumer.Handle(
            new PlanChangeUpgradeCancelledIntegrationEvent
            {
                TenantId = tenantId,
                PlanChangeRequestId = Guid.NewGuid(),
                SaaSPaymentId = payment.Id,
            },
            payments,
            unitOfWork,
            new FakeCorrelationContext(),
            NullLogger<SaaSPayment>.Instance,
            CancellationToken.None
        );

        Assert.Equal(PaymentStatus.Succeeded, payment.Status);
        Assert.Equal(0, unitOfWork.SaveCalls);
    }

    [Fact]
    public async Task An_event_without_SaaSPaymentId_is_ignored()
    {
        // El intent nunca llegó al provider (p.ej. plan gratuito o fallo pre-checkout): no hay pago que marcar.
        var payments = new FakeSaaSPaymentRepository();
        var unitOfWork = new FakeUnitOfWork();

        await CheckoutCancelledOrExpiredSaaSPaymentConsumer.Handle(
            new PlanChangeUpgradeExpiredIntegrationEvent
            {
                TenantId = Guid.NewGuid(),
                PlanChangeRequestId = Guid.NewGuid(),
                SaaSPaymentId = null,
            },
            payments,
            unitOfWork,
            new FakeCorrelationContext(),
            NullLogger<SaaSPayment>.Instance,
            CancellationToken.None
        );

        Assert.Equal(0, unitOfWork.SaveCalls);
    }

    private static SaaSPayment ProcessingPayment(Guid tenantId)
    {
        var payment = SaaSPayment
            .Create(
                tenantId,
                IdempotencyKey.Create($"cancel-consumer-{Guid.NewGuid():N}").Value,
                Money.Create(5000, "USD").Value,
                SaaSPaymentType.SeatsPurchaseCharge,
                Guid.NewGuid(),
                PaymentProviderCode.Stripe,
                StatementDescriptor.Create("TAXVISION").Value,
                Guid.Empty,
                DateTime.UtcNow
            )
            .Value;

        payment.RecordHostedCheckoutSession(
            "cs_test",
            ExternalPaymentReference.Create(PaymentProviderCode.Stripe, "pi_test").Value,
            "https://checkout.example.com/test",
            DateTime.UtcNow
        );

        return payment;
    }
}
