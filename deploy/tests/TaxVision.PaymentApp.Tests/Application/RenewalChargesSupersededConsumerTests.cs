using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.PaymentApp.Application.SaaSPayments.IntegrationEvents;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.ValueObjects;
using TaxVision.PaymentApp.Tests.TestDoubles;

namespace TaxVision.PaymentApp.Tests.Application;

/// <summary>Si el tenant ya pagó a mano, el cobro automático que seguía reintentando se cancela.</summary>
public sealed class RenewalChargesSupersededConsumerTests
{
    private readonly Guid _tenantId = Guid.NewGuid();

    [Fact]
    public async Task Cancels_a_charge_that_was_still_going_to_be_retried()
    {
        var payment = FailedPayment(_tenantId, willRetry: true);
        var unitOfWork = new FakeUnitOfWork();

        await Handle(payment, unitOfWork);

        Assert.Equal(PaymentStatus.Cancelled, payment.Status);
        Assert.Null(payment.NextRetryAtUtc);
        Assert.Equal(1, unitOfWork.SaveCalls);
    }

    [Fact]
    public async Task Ignores_a_charge_of_another_tenant()
    {
        var payment = FailedPayment(Guid.NewGuid(), willRetry: true);
        var unitOfWork = new FakeUnitOfWork();

        await Handle(payment, unitOfWork);

        Assert.Equal(PaymentStatus.Failed, payment.Status);
        Assert.Equal(0, unitOfWork.SaveCalls);
    }

    [Fact]
    public async Task Leaves_an_abandoned_failure_as_failed()
    {
        var payment = FailedPayment(_tenantId, willRetry: false);

        await Handle(payment, new FakeUnitOfWork());

        Assert.Equal(PaymentStatus.Failed, payment.Status);
    }

    [Fact]
    public async Task Never_touches_a_charge_in_flight()
    {
        var payment = PendingPayment(_tenantId);
        payment.MarkProcessing(
            ExternalPaymentReference.Create(PaymentProviderCode.Stripe, "pi_in_flight").Value,
            "processing",
            providerResponseBody: null,
            Guid.Empty,
            DateTime.UtcNow
        );

        await Handle(payment, new FakeUnitOfWork());

        Assert.Equal(PaymentStatus.Processing, payment.Status);
    }

    private Task Handle(SaaSPayment payment, FakeUnitOfWork unitOfWork) =>
        RenewalChargesSupersededConsumer.Handle(
            new RenewalChargesSupersededIntegrationEvent
            {
                TenantId = _tenantId,
                IdempotencyKeys = [payment.IdempotencyKey.Value],
            },
            new FakeSaaSPaymentRepository(payment),
            unitOfWork,
            new FakeCorrelationContext(),
            NullLogger<SaaSPayment>.Instance,
            CancellationToken.None
        );

    private static SaaSPayment FailedPayment(Guid tenantId, bool willRetry)
    {
        var payment = PendingPayment(tenantId);
        payment.MarkFailed(
            "card_declined",
            "Declined.",
            willRetry,
            willRetry ? DateTime.UtcNow.AddHours(1) : null,
            Guid.Empty,
            DateTime.UtcNow
        );
        return payment;
    }

    private static SaaSPayment PendingPayment(Guid tenantId) =>
        SaaSPayment
            .Create(
                tenantId,
                IdempotencyKey.Create($"subscription-renewal-{Guid.NewGuid():N}-20261101").Value,
                Money.Create(2900, "USD").Value,
                SaaSPaymentType.SubscriptionRenewal,
                Guid.NewGuid(),
                PaymentProviderCode.Stripe,
                StatementDescriptor.Create("TAXVISION SAAS").Value,
                Guid.Empty,
                DateTime.UtcNow
            )
            .Value;
}
