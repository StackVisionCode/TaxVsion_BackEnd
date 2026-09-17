using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using TaxVision.Subscription.Application.Subscriptions.IntegrationEvents;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;
using TaxVision.Subscription.Tests.TestDoubles;
using Xunit;

namespace TaxVision.Subscription.Tests.Application;

public sealed class SubscriptionStatusChangedPublishingTests
{
    [Fact]
    public async Task Publishes_status_changed_with_previous_status_reason_and_ids()
    {
        var tenantId = Guid.NewGuid();
        var nowUtc = DateTime.UtcNow;
        var subscription = BuildActiveSubscription(tenantId, nowUtc);
        subscription.SuspendForPolicyViolation("test", Guid.Empty, nowUtc); // Active -> Suspended

        var bus = new CapturingMessageBus();
        await bus.PublishStatusChangedAsync(
            subscription,
            previousStatus: SubscriptionStatus.Active,
            reason: SubscriptionChangeReason.AdminSuspended,
            failureCode: "card_declined"
        );

        var evt = Assert.Single(bus.Published.OfType<TenantSubscriptionStatusChangedIntegrationEvent>());
        Assert.Equal(tenantId, evt.TenantId);
        Assert.Equal(subscription.Id, evt.TenantSubscriptionId);
        Assert.Equal("Suspended", evt.Status);
        Assert.Equal("Active", evt.PreviousStatus);
        Assert.Equal("AdminSuspended", evt.Reason);
        Assert.Equal("card_declined", evt.FailureCode);
    }

    private static TenantSubscription BuildActiveSubscription(Guid tenantId, DateTime nowUtc)
    {
        var plan = SubscriptionPlan
            .Create(PlanCode.Create("starter").Value, "Starter", "Starter plan", PlanTier.Standard, Guid.Empty, nowUtc)
            .Value;
        var version = SubscriptionPlanVersion.Create(plan.Id, 1, 14, [BillingCycle.Monthly, BillingCycle.Yearly]).Value;
        version.AddPriceTier(
            PlanPriceTier.Create(version.Id, BillingCycle.Monthly, 1, null, Money.Create(49m, "USD").Value).Value
        );
        plan.AddVersion(version, Guid.Empty, nowUtc);
        plan.PublishVersion(version.Id, nowUtc, Guid.Empty, nowUtc);

        return TenantSubscription
            .ActivateImmediately(
                tenantId,
                plan,
                plan.GetPublishedVersion()!,
                BillingCycle.Monthly,
                nowUtc.AddDays(-10),
                nowUtc.AddDays(20),
                Guid.Empty,
                nowUtc
            )
            .Value;
    }
}
