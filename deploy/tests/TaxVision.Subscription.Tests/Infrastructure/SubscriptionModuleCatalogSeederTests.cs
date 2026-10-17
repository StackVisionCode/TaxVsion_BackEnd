using BuildingBlocks.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Subscription.Domain.Catalog;
using TaxVision.Subscription.Infrastructure.Configuration;
using TaxVision.Subscription.Infrastructure.Persistence;
using Xunit;

namespace TaxVision.Subscription.Tests.Infrastructure;

public sealed class SubscriptionModuleCatalogSeederTests
{
    [Fact]
    public async Task Empty_catalog_is_seeded_when_passive_mode_is_enabled()
    {
        await using var db = CreateContext();

        await SeedAsync(db, seedOnlyIfEmpty: true);

        Assert.Equal(SubscriptionModuleCatalogSeeder.Definitions.Count, await db.Modules.CountAsync());
    }

    [Fact]
    public async Task Existing_catalog_is_not_modified_in_passive_mode()
    {
        await using var db = CreateContext();
        var definition = SubscriptionModuleCatalogSeeder.Definitions[0];
        var module = Module.Seed(definition.Id, definition.Code, definition.Name, definition.Description).Value;
        module.MarkAsCommercial();
        module.SetAsAddon(7m, 70m);
        db.Modules.Add(module);
        await db.SaveChangesAsync();

        await SeedAsync(db, seedOnlyIfEmpty: true);

        var persisted = await db.Modules.SingleAsync();
        Assert.Equal(ModuleCategory.Commercial, persisted.Category);
        Assert.Equal(7m, persisted.AddonPriceMonthlyUsd);
    }

    [Fact]
    public async Task Explicit_reconciliation_restores_seeded_configuration()
    {
        await using var db = CreateContext();
        await SeedAsync(db, seedOnlyIfEmpty: false);
        var definition = SubscriptionModuleCatalogSeeder.Definitions.First(item =>
            item.Category == ModuleCategory.Core
        );
        var module = await db.Modules.SingleAsync(item => item.Code == definition.Code);
        module.MarkAsCommercial();
        module.SetAsAddon(7m, 70m);
        await db.SaveChangesAsync();

        await SeedAsync(db, seedOnlyIfEmpty: false);

        Assert.Equal(ModuleCategory.Core, module.Category);
        Assert.Null(module.AddonPriceMonthlyUsd);
        Assert.Null(module.AddonPriceYearlyUsd);
    }

    private static SubscriptionDbContext CreateContext() =>
        new(
            new DbContextOptionsBuilder<SubscriptionDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options,
            new EmptyTenantContext()
        );

    private static Task SeedAsync(SubscriptionDbContext db, bool seedOnlyIfEmpty) =>
        SubscriptionModuleCatalogSeeder.SeedAsync(
            db,
            new SubscriptionCatalogOptions
            {
                ModuleCatalog = new ModuleCatalogOptions { SeedOnlyIfEmpty = seedOnlyIfEmpty },
            },
            NullLogger.Instance,
            CancellationToken.None
        );

    private sealed class EmptyTenantContext : ITenantContext
    {
        public Guid TenantId => throw new InvalidOperationException("TenantId is not set.");
        public bool HasTenant => false;

        public void SetTenant(Guid tenantId) { }
    }
}
