using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using TaxVision.Subscription.Application.Subscriptions.IntegrationEvents;
using TaxVision.Subscription.Tests.TestDoubles;
using Xunit;

namespace TaxVision.Subscription.Tests.Application;

/// <summary>Observabilidad (Fase 6): el consumer de métricas registra cada transición de estado
/// (from/to/reason) desde el evento de ciclo de vida — un único punto para todas las transiciones.</summary>
public sealed class SubscriptionLifecycleMetricsConsumerTests
{
    [Fact]
    public async Task Records_the_status_transition_with_from_to_and_reason()
    {
        var metrics = new FakeSubscriptionMetrics();

        await SubscriptionLifecycleMetricsConsumer.Handle(
            new TenantSubscriptionStatusChangedIntegrationEvent
            {
                TenantId = Guid.NewGuid(),
                TenantSubscriptionId = Guid.NewGuid(),
                Status = "GracePeriod",
                PreviousStatus = "PastDue",
                Reason = nameof(SubscriptionChangeReason.RenewalPaymentFailed),
            },
            metrics
        );

        var (from, to, reason) = Assert.Single(metrics.StatusTransitions);
        Assert.Equal("PastDue", from);
        Assert.Equal("GracePeriod", to);
        Assert.Equal(nameof(SubscriptionChangeReason.RenewalPaymentFailed), reason);
    }
}
