using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Subscription.Api.Bootstrap;
using TaxVision.Subscription.Domain.Entitlements;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.ValueObjects;
using TaxVision.Subscription.Infrastructure.Configuration;
using TaxVision.Subscription.Infrastructure.Scheduling;
using TaxVision.Subscription.Tests.TestDoubles;
using Xunit;

namespace TaxVision.Subscription.Tests.Application;

public sealed class SubscriptionCatalogReconcilerTests
{
    private const string LockKey = "bootstrap:catalog-reconcile";

    // SUB-8: con dos réplicas arrancando a la vez, solo una reconcilia.
    [Fact]
    public async Task LockHeldByOtherReplica_SkipsReconcile()
    {
        var lockFactory = new FakeDistributedLockFactory();
        lockFactory.Hold(LockKey);
        var scopes = new ThrowingScopeFactory();

        await RunAsync(scopes, lockFactory);

        Assert.Equal(0, scopes.CreatedScopes);
    }

    [Fact]
    public async Task LockFree_ReconcilesAndReleases()
    {
        var lockFactory = new FakeDistributedLockFactory();
        var scopes = new ThrowingScopeFactory();

        await RunAsync(scopes, lockFactory);

        Assert.Equal(1, scopes.CreatedScopes);
        Assert.Contains(LockKey, lockFactory.RequestedKeys);
        Assert.False(lockFactory.IsHeld(LockKey));
    }

    private static async Task RunAsync(IServiceScopeFactory scopes, IDistributedLockFactory lockFactory)
    {
        var reconciler = new SubscriptionCatalogReconciler(
            scopes,
            new StartedLifetime(),
            NullLogger<SubscriptionCatalogReconciler>.Instance,
            Options.Create(new SubscriptionCatalogOptions()),
            lockFactory
        );

        await reconciler.StartAsync(CancellationToken.None);
        await reconciler.StopAsync(CancellationToken.None);
    }

    /// <summary>Si se llega a abrir un scope, la reconciliación empezó; la excepción la absorbe el arranque diferido.</summary>
    private sealed class ThrowingScopeFactory : IServiceScopeFactory
    {
        public int CreatedScopes { get; private set; }

        public IServiceScope CreateScope()
        {
            CreatedScopes++;
            throw new InvalidOperationException("Reconcile started.");
        }
    }

    private sealed class StartedLifetime : IHostApplicationLifetime
    {
        public CancellationToken ApplicationStarted => new(canceled: true);
        public CancellationToken ApplicationStopping => CancellationToken.None;
        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void StopApplication() { }
    }

    [Fact]
    public void Matching_modules_do_not_need_revision()
    {
        var current = SubscriptionCatalogReconciler.PublishedModulesOf(PublishedPlanWith("customers", "documents"));

        Assert.NotNull(current);
        Assert.False(SubscriptionCatalogReconciler.NeedsRevision(current, ["documents", "customers"]));
    }

    [Fact]
    public void Added_or_removed_modules_need_revision()
    {
        var current = SubscriptionCatalogReconciler.PublishedModulesOf(PublishedPlanWith("customers"));

        Assert.NotNull(current);
        Assert.True(SubscriptionCatalogReconciler.NeedsRevision(current, ["customers", "comms"]));
        Assert.True(SubscriptionCatalogReconciler.NeedsRevision(current, []));
    }

    [Fact]
    public void Only_module_features_are_compared()
    {
        var plan = PublishedPlanWith("customers");
        var published = plan.Versions.Single();
        published.AddFeature(
            PlanFeature.Create(published.Id, EntitlementKey.Create("seats.max").Value, true, "seats.max").Value
        );

        var current = SubscriptionCatalogReconciler.PublishedModulesOf(plan);

        Assert.Equal(["customers"], current!.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void Plan_without_published_version_has_no_module_snapshot()
    {
        var plan = SubscriptionPlan
            .Create(
                PlanCode.Create("starter").Value,
                "Starter",
                "Starter",
                PlanTier.Starter,
                Guid.Empty,
                DateTime.UtcNow
            )
            .Value;

        Assert.Null(SubscriptionCatalogReconciler.PublishedModulesOf(plan));
    }

    private static SubscriptionPlan PublishedPlanWith(params string[] modules)
    {
        var nowUtc = DateTime.UtcNow;
        var plan = SubscriptionPlan
            .Create(PlanCode.Create("starter").Value, "Starter", "Starter", PlanTier.Starter, Guid.Empty, nowUtc)
            .Value;
        var version = SubscriptionPlanVersion.Create(plan.Id, 1, 14, [BillingCycle.Monthly]).Value;

        foreach (var module in modules)
        {
            version.AddFeature(
                PlanFeature
                    .Create(version.Id, EntitlementKey.Create($"module.{module}").Value, true, $"module.{module}")
                    .Value
            );
        }

        version.AddPriceTier(
            PlanPriceTier.Create(version.Id, BillingCycle.Monthly, 1, null, Money.Create(49m, "USD").Value).Value
        );
        plan.AddVersion(version, Guid.Empty, nowUtc);
        plan.PublishVersion(version.Id, nowUtc, Guid.Empty, nowUtc);
        return plan;
    }
}
