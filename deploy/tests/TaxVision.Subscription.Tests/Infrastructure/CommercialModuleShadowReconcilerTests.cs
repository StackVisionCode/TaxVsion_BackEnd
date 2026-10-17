using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Subscription.Domain.AddOns;
using TaxVision.Subscription.Domain.Catalog;
using TaxVision.Subscription.Domain.Entitlements;
using TaxVision.Subscription.Domain.ValueObjects;
using TaxVision.Subscription.Infrastructure.Catalog;
using TaxVision.Subscription.Infrastructure.Catalog.Concurrency;
using TaxVision.Subscription.Infrastructure.Persistence;
using Xunit;

namespace TaxVision.Subscription.Tests.Infrastructure;

public sealed class CommercialModuleShadowReconcilerTests
{
    [Fact]
    public async Task Upsert_is_idempotent_and_builds_the_canonical_shadow()
    {
        await using var db = CreateContext();
        db.Modules.Add(CommercialModule());
        await db.SaveChangesAsync();
        var reconciler = CreateReconciler(db);

        await reconciler.UpsertShadowForAsync("wallet", CancellationToken.None);
        await reconciler.UpsertShadowForAsync("wallet", CancellationToken.None);

        var shadow = await db
            .AddOnDefinitions.Include(item => item.Features)
            .Include(item => item.PriceTiers)
            .SingleAsync();
        Assert.Equal("module-addon-wallet", shadow.Code.Value);
        Assert.Equal(AddOnDefinitionStatus.Published, shadow.Status);
        var feature = Assert.Single(shadow.Features);
        Assert.Equal("module.wallet", feature.FeatureKey.Value);
        Assert.True(feature.Enabled);
        Assert.Equal(2, shadow.PriceTiers.Count);
        Assert.Contains(
            shadow.PriceTiers,
            tier => tier.BillingCycle == BillingCycle.Monthly && tier.UnitAmount.Amount == 15m
        );
        Assert.Contains(
            shadow.PriceTiers,
            tier => tier.BillingCycle == BillingCycle.Yearly && tier.UnitAmount.Amount == 150m
        );
    }

    [Fact]
    public async Task Upsert_repairs_feature_prices_metadata_and_status()
    {
        await using var db = CreateContext();
        var module = CommercialModule();
        db.Modules.Add(module);
        var malformed = MalformedShadow();
        db.AddOnDefinitions.Add(malformed);
        await db.SaveChangesAsync();
        var reconciler = CreateReconciler(db);

        await reconciler.UpsertShadowForAsync("wallet", CancellationToken.None);

        var shadow = await db
            .AddOnDefinitions.Include(item => item.Features)
            .Include(item => item.PriceTiers)
            .SingleAsync();
        Assert.Equal(AddOnDefinitionStatus.Published, shadow.Status);
        Assert.Equal(module.Name, shadow.Name);
        Assert.Equal(module.Description, shadow.Description);
        Assert.Equal("module.wallet", Assert.Single(shadow.Features).FeatureKey.Value);
        Assert.Equal(2, shadow.PriceTiers.Count);
        Assert.Contains(
            shadow.PriceTiers,
            tier => tier.BillingCycle == BillingCycle.Monthly && tier.UnitAmount.Amount == 15m
        );
        Assert.Contains(
            shadow.PriceTiers,
            tier => tier.BillingCycle == BillingCycle.Yearly && tier.UnitAmount.Amount == 150m
        );
    }

    [Fact]
    public async Task Deprecate_preserves_existing_tenant_purchase()
    {
        await using var db = CreateContext();
        var module = CommercialModule();
        db.Modules.Add(module);
        await db.SaveChangesAsync();
        var reconciler = CreateReconciler(db);
        await reconciler.UpsertShadowForAsync("wallet", CancellationToken.None);
        var shadow = await db.AddOnDefinitions.SingleAsync();
        var tenantAddOn = TenantAddOn
            .Purchase(
                Guid.NewGuid(),
                shadow,
                1,
                Money.Create(15m, "USD").Value,
                BillingCycle.Monthly,
                true,
                Guid.NewGuid(),
                DateTime.UtcNow
            )
            .Value;
        db.TenantAddOns.Add(tenantAddOn);
        await db.SaveChangesAsync();

        await reconciler.DeprecateShadowForAsync("wallet", CancellationToken.None);

        Assert.Equal(AddOnDefinitionStatus.Deprecated, shadow.Status);
        Assert.Equal(1, await db.TenantAddOns.IgnoreQueryFilters().CountAsync());
        Assert.Equal(AddOnStatus.Active, tenantAddOn.Status);
    }

    private static CommercialModuleShadowReconciler CreateReconciler(SubscriptionDbContext db) =>
        new(db, new InMemoryModuleEditSerializer(), NullLogger<CommercialModuleShadowReconciler>.Instance);

    private static Module CommercialModule()
    {
        var module = Module.Create("wallet", "Wallet", "Wallet module").Value;
        module.MarkAsCommercial();
        module.SetAsAddon(15m, 150m);
        return module;
    }

    private static AddOnDefinition MalformedShadow()
    {
        var nowUtc = DateTime.UtcNow;
        var shadow = AddOnDefinition
            .Create(
                AddOnCode.Create("module-addon-wallet").Value,
                "Old name",
                "Old description",
                "module",
                false,
                [BillingCycle.Monthly, BillingCycle.Yearly],
                Guid.Empty,
                nowUtc
            )
            .Value;
        shadow.AddFeature(AddOnFeature.Create(shadow.Id, EntitlementKey.Create("module.wrong").Value, true).Value);
        shadow.AddPriceTier(
            AddOnPriceTier.Create(shadow.Id, BillingCycle.Monthly, 1, null, Money.Create(1m, "USD").Value).Value
        );
        shadow.Publish(Guid.Empty, nowUtc);
        shadow.Deprecate(Guid.Empty, nowUtc);
        return shadow;
    }

    private static SubscriptionDbContext CreateContext() =>
        new(
            new DbContextOptionsBuilder<SubscriptionDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new EmptyTenantContext()
        );

    private sealed class EmptyTenantContext : ITenantContext
    {
        public Guid TenantId => throw new InvalidOperationException("TenantId is not set.");
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }
}
