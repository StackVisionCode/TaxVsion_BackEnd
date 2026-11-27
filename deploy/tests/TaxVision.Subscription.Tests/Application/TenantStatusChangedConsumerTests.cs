using BuildingBlocks.Messaging;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Subscription.Application.Entitlements.Commands.RecalculateEntitlements;
using TaxVision.Subscription.Application.Subscriptions.IntegrationEvents;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;
using TaxVision.Subscription.Tests.TestDoubles;
using Xunit;

namespace TaxVision.Subscription.Tests.Application;

/// <summary>Suspender o cerrar la oficina en Tenant retiene su cobro; reactivarla lo libera.</summary>
public sealed class TenantStatusChangedConsumerTests
{
    private static readonly DateTime Now = DateTime.UtcNow;

    [Fact]
    public async Task Suspending_the_office_holds_its_billing()
    {
        var subscription = ActiveSubscription();
        var unitOfWork = new FakeUnitOfWork();

        await Handle(Event(subscription.TenantId, "Suspended", isActive: false, version: 2), subscription, unitOfWork);

        Assert.True(subscription.IsBillingHeld);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Changing_the_hold_recalculates_the_office_modules()
    {
        var subscription = ActiveSubscription();
        var bus = new CapturingMessageBus();

        await TenantStatusChangedConsumer.Handle(
            Event(subscription.TenantId, "Suspended", isActive: false, version: 2),
            new FakeSubscriptionRepo(subscription),
            new FakeUnitOfWork(),
            bus,
            NullLogger<TenantSubscription>.Instance,
            CancellationToken.None
        );

        Assert.Contains(
            bus.Published,
            message => message is RecalculateEntitlementsCommand command && command.TenantId == subscription.TenantId
        );
    }

    [Fact]
    public async Task Reactivating_the_office_releases_the_hold()
    {
        var subscription = ActiveSubscription();
        subscription.ApplyTenantStatus(tenantIsActive: false, tenantVersion: 2, Now);
        var unitOfWork = new FakeUnitOfWork();

        await Handle(Event(subscription.TenantId, "Active", isActive: true, version: 3), subscription, unitOfWork);

        Assert.False(subscription.IsBillingHeld);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task The_platform_office_and_offices_without_subscription_are_ignored()
    {
        var unitOfWork = new FakeUnitOfWork();

        await Handle(Event(PlatformTenant.Id, "Suspended", isActive: false, version: 1), null, unitOfWork);
        await Handle(Event(Guid.NewGuid(), "Suspended", isActive: false, version: 1), null, unitOfWork);

        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    private static Task Handle(
        TenantStatusChangedIntegrationEvent evt,
        TenantSubscription? subscription,
        FakeUnitOfWork unitOfWork
    ) =>
        TenantStatusChangedConsumer.Handle(
            evt,
            new FakeSubscriptionRepo(subscription),
            unitOfWork,
            new CapturingMessageBus(),
            NullLogger<TenantSubscription>.Instance,
            CancellationToken.None
        );

    private static TenantStatusChangedIntegrationEvent Event(
        Guid tenantId,
        string status,
        bool isActive,
        long version
    ) =>
        new()
        {
            ChangedTenantId = tenantId,
            Status = status,
            IsActive = isActive,
            TenantVersion = version,
        };

    private static TenantSubscription ActiveSubscription()
    {
        var plan = SubscriptionPlan
            .Create(PlanCode.Create("enterprise").Value, "Enterprise", "plan", PlanTier.Starter, Guid.Empty, Now)
            .Value;
        var version = SubscriptionPlanVersion
            .Create(plan.Id, 1, trialDaysDefault: 14, [BillingCycle.Monthly, BillingCycle.Yearly])
            .Value;
        plan.AddVersion(version, Guid.Empty, Now);
        plan.PublishVersion(version.Id, Now, Guid.Empty, Now);
        return TenantSubscription
            .ActivateImmediately(
                Guid.NewGuid(),
                plan,
                version,
                BillingCycle.Monthly,
                Now,
                Now.AddDays(30),
                Guid.Empty,
                Now
            )
            .Value;
    }
}
