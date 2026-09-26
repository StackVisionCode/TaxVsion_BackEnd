using BuildingBlocks.Messaging.PaymentAppIntegrationEvents;
using TaxVision.PaymentApp.Application.SaaSPayments.Common;
using TaxVision.PaymentApp.Domain.SaaSPayments;
using TaxVision.PaymentApp.Domain.ValueObjects;
using TaxVision.PaymentApp.Tests.TestDoubles;

namespace TaxVision.PaymentApp.Tests.Application;

public sealed class SaaSPaymentResultPublisherTests
{
    private static readonly Guid TenantId = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid TargetId = Guid.Parse("44444444-4444-4444-4444-444444444444");

    [Fact]
    public async Task Succeeded_subscription_renewal_publishes_the_renewal_success()
    {
        var payment = ProcessingPayment(SaaSPaymentType.SubscriptionRenewal);
        payment.MarkSucceeded(DateTime.UtcNow, Guid.Empty);
        var bus = new CapturingMessageBus();

        await SaaSPaymentResultPublisher.PublishAsync(payment, bus, "corr", CancellationToken.None);

        var published = Assert.Single(bus.Published.OfType<SubscriptionRenewalPaymentSucceededIntegrationEvent>());
        Assert.Equal(TenantId, published.TenantId);
        Assert.Equal(TargetId, published.TenantSubscriptionId);
        Assert.Equal("pi_test_1", published.ExternalPaymentReference);
        Assert.Equal("corr", published.CorrelationId);
    }

    [Fact]
    public async Task Failed_seat_renewal_publishes_the_failure_with_its_retry_schedule()
    {
        var payment = ProcessingPayment(SaaSPaymentType.SeatRenewal);
        var nextRetryAtUtc = DateTime.UtcNow.AddHours(1);
        payment.MarkFailed("card_declined", "Declined.", willRetry: true, nextRetryAtUtc, Guid.Empty, DateTime.UtcNow);
        var bus = new CapturingMessageBus();

        await SaaSPaymentResultPublisher.PublishAsync(payment, bus, "corr", CancellationToken.None);

        var published = Assert.Single(bus.Published.OfType<SeatRenewalPaymentFailedIntegrationEvent>());
        Assert.Equal(TargetId, published.SeatId);
        Assert.Equal("card_declined", published.FailureCode);
        Assert.True(published.WillRetry);
        Assert.Equal(nextRetryAtUtc, published.NextRetryAtUtc);
    }

    [Fact]
    public async Task Provider_cancelled_add_on_renewal_is_published_as_a_failure()
    {
        var payment = ProcessingPayment(SaaSPaymentType.AddOnRenewal);
        payment.CancelByAdmin("ProviderCancelled", Guid.Empty, DateTime.UtcNow);
        var bus = new CapturingMessageBus();

        await SaaSPaymentResultPublisher.PublishAsync(payment, bus, "corr", CancellationToken.None);

        var published = Assert.Single(bus.Published.OfType<AddOnRenewalPaymentFailedIntegrationEvent>());
        Assert.Equal(TargetId, published.TenantAddOnId);
        Assert.False(published.WillRetry);
    }

    [Fact]
    public async Task Succeeded_plan_change_publishes_the_plan_change_success()
    {
        var payment = ProcessingPayment(SaaSPaymentType.PlanChangeCharge);
        payment.MarkSucceeded(DateTime.UtcNow, Guid.Empty);
        var bus = new CapturingMessageBus();

        await SaaSPaymentResultPublisher.PublishAsync(payment, bus, "corr", CancellationToken.None);

        var published = Assert.Single(bus.Published.OfType<SubscriptionPlanChangePaymentSucceededIntegrationEvent>());
        Assert.Equal(TargetId, published.PlanChangeRequestId);
    }

    [Theory]
    [InlineData(SaaSPaymentType.SubscriptionRenewal)]
    [InlineData(SaaSPaymentType.SeatRenewal)]
    [InlineData(SaaSPaymentType.AddOnRenewal)]
    [InlineData(SaaSPaymentType.PlanChangeCharge)]
    [InlineData(SaaSPaymentType.SeatsPurchaseCharge)]
    [InlineData(SaaSPaymentType.SubscriptionRenewalCheckout)]
    [InlineData(SaaSPaymentType.AddOnPurchaseCharge)]
    [InlineData(SaaSPaymentType.PlanChangeCheckout)]
    public async Task A_payment_that_is_still_processing_publishes_nothing(SaaSPaymentType type)
    {
        var payment = ProcessingPayment(type);
        var bus = new CapturingMessageBus();

        await SaaSPaymentResultPublisher.PublishAsync(payment, bus, "corr", CancellationToken.None);

        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task Succeeded_seats_checkout_is_routed_to_its_own_result_event()
    {
        var payment = ProcessingPayment(SaaSPaymentType.SeatsPurchaseCharge);
        payment.MarkSucceeded(DateTime.UtcNow, Guid.Empty);
        var bus = new CapturingMessageBus();

        await SaaSPaymentResultPublisher.PublishAsync(payment, bus, "corr", CancellationToken.None);

        var published = Assert.Single(bus.Published.OfType<SeatsCheckoutPaidIntegrationEvent>());
        Assert.Equal(TargetId, published.SeatPurchaseIntentId);
    }

    // La compra de un add-on por checkout tiene su propio evento: no se mezcla con la renovación del add-on.
    [Fact]
    public async Task Succeeded_add_on_checkout_is_routed_to_its_own_result_event()
    {
        var payment = ProcessingPayment(SaaSPaymentType.AddOnPurchaseCharge);
        payment.MarkSucceeded(DateTime.UtcNow, Guid.Empty);
        var bus = new CapturingMessageBus();

        await SaaSPaymentResultPublisher.PublishAsync(payment, bus, "corr", CancellationToken.None);

        var published = Assert.Single(bus.Published.OfType<AddOnCheckoutPaidIntegrationEvent>());
        Assert.Equal(TargetId, published.AddOnPurchaseIntentId);
        Assert.Equal("pi_test_1", published.ProviderPaymentReference);
    }

    [Fact]
    public async Task Cancelled_add_on_checkout_is_published_as_a_failure()
    {
        var payment = ProcessingPayment(SaaSPaymentType.AddOnPurchaseCharge);
        payment.CancelByAdmin("ProviderCancelled", Guid.Empty, DateTime.UtcNow);
        var bus = new CapturingMessageBus();

        await SaaSPaymentResultPublisher.PublishAsync(payment, bus, "corr", CancellationToken.None);

        var published = Assert.Single(bus.Published.OfType<AddOnCheckoutFailedIntegrationEvent>());
        Assert.Equal(TargetId, published.AddOnPurchaseIntentId);
    }

    // El checkout y el cobro off-session del upgrade cierran el MISMO PlanChangeRequest: un solo evento y
    // un solo consumer para los dos caminos.
    [Fact]
    public async Task Succeeded_plan_change_checkout_publishes_the_same_event_as_the_off_session_charge()
    {
        var payment = ProcessingPayment(SaaSPaymentType.PlanChangeCheckout);
        payment.MarkSucceeded(DateTime.UtcNow, Guid.Empty);
        var bus = new CapturingMessageBus();

        await SaaSPaymentResultPublisher.PublishAsync(payment, bus, "corr", CancellationToken.None);

        var published = Assert.Single(bus.Published.OfType<SubscriptionPlanChangePaymentSucceededIntegrationEvent>());
        Assert.Equal(TargetId, published.PlanChangeRequestId);
    }

    private static SaaSPayment ProcessingPayment(SaaSPaymentType type)
    {
        var payment = SaaSPayment
            .Create(
                TenantId,
                IdempotencyKey.Create($"key-{type}").Value,
                Money.Create(1999, "USD").Value,
                type,
                TargetId,
                PaymentProviderCode.Stripe,
                StatementDescriptor.Create("TAXVISION SAAS").Value,
                Guid.Empty,
                DateTime.UtcNow
            )
            .Value;

        payment.MarkProcessing(
            ExternalPaymentReference.Create(PaymentProviderCode.Stripe, "pi_test_1").Value,
            "processing",
            providerResponseBody: null,
            Guid.Empty,
            DateTime.UtcNow
        );
        return payment;
    }
}
