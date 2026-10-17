using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using TaxVision.Subscription.Application.Catalog.Commands.UpdateModule;
using TaxVision.Subscription.Application.Entitlements.Commands.RecalculateEntitlementsForAllPlans;
using TaxVision.Subscription.Domain.Catalog;
using TaxVision.Subscription.Infrastructure.Catalog.Concurrency;
using TaxVision.Subscription.Tests.TestDoubles;
using Xunit;

namespace TaxVision.Subscription.Tests.Application;

public sealed class UpdateModuleHandlerTests
{
    [Fact]
    public async Task Converts_core_module_to_sellable_commercial_module()
    {
        var module = Module.Create("wallet", "Wallet", "Wallet module").Value;
        var unitOfWork = new FakeUnitOfWork();
        var bus = new CapturingMessageBus();

        var result = await UpdateModuleHandler.Handle(
            new UpdateModuleCommand("WALLET", "Commercial", 15m, 150m, Guid.NewGuid()),
            new FakeModuleRepo(module),
            new InMemoryModuleEditSerializer(),
            unitOfWork,
            bus,
            new FakeCorrelationContext(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal(ModuleCategory.Commercial, module.Category);
        Assert.Equal(15m, module.AddonPriceMonthlyUsd);
        Assert.Equal(150m, module.AddonPriceYearlyUsd);
        Assert.True(module.IsSellableAsAddon);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
        var changed = Assert.Single(bus.Published.OfType<ModuleCategoryChangedIntegrationEvent>());
        Assert.Equal("Core", changed.OldCategory);
        Assert.Equal("Commercial", changed.NewCategory);
        Assert.Equal("test-correlation-id", changed.CorrelationId);
        Assert.Single(bus.Published.OfType<RecalculateEntitlementsForAllPlansCommand>());
    }

    [Fact]
    public async Task Converts_commercial_module_to_core_and_clears_prices()
    {
        var module = CommercialModule();
        var bus = new CapturingMessageBus();

        var result = await UpdateModuleHandler.Handle(
            new UpdateModuleCommand("wallet", "Core", null, null, Guid.NewGuid()),
            new FakeModuleRepo(module),
            new InMemoryModuleEditSerializer(),
            new FakeUnitOfWork(),
            bus,
            new FakeCorrelationContext(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(ModuleCategory.Core, module.Category);
        Assert.Null(module.AddonPriceMonthlyUsd);
        Assert.Null(module.AddonPriceYearlyUsd);
        Assert.Single(bus.Published.OfType<RecalculateEntitlementsForAllPlansCommand>());
    }

    [Fact]
    public async Task Rejects_incomplete_commercial_prices_without_saving()
    {
        var unitOfWork = new FakeUnitOfWork();
        var bus = new CapturingMessageBus();

        var result = await UpdateModuleHandler.Handle(
            new UpdateModuleCommand("wallet", "Commercial", 15m, null, Guid.NewGuid()),
            new FakeModuleRepo(Module.Create("wallet", "Wallet", "Wallet module").Value),
            new InMemoryModuleEditSerializer(),
            unitOfWork,
            bus,
            new FakeCorrelationContext(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Module.IncompleteAddonPricing", result.Error.Code);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
        Assert.Empty(bus.Published);
    }

    [Fact]
    public async Task Price_only_change_syncs_catalog_without_global_entitlement_recalculation()
    {
        var module = CommercialModule();
        var bus = new CapturingMessageBus();

        var result = await UpdateModuleHandler.Handle(
            new UpdateModuleCommand("wallet", "Commercial", 20m, 200m, Guid.NewGuid()),
            new FakeModuleRepo(module),
            new InMemoryModuleEditSerializer(),
            new FakeUnitOfWork(),
            bus,
            new FakeCorrelationContext(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Single(bus.Published.OfType<ModuleCategoryChangedIntegrationEvent>());
        Assert.Empty(bus.Published.OfType<RecalculateEntitlementsForAllPlansCommand>());
    }

    [Fact]
    public async Task No_op_does_not_save_or_publish()
    {
        var unitOfWork = new FakeUnitOfWork();
        var bus = new CapturingMessageBus();

        var result = await UpdateModuleHandler.Handle(
            new UpdateModuleCommand("wallet", "Core", null, null, Guid.NewGuid()),
            new FakeModuleRepo(Module.Create("wallet", "Wallet", "Wallet module").Value),
            new InMemoryModuleEditSerializer(),
            unitOfWork,
            bus,
            new FakeCorrelationContext(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
        Assert.Empty(bus.Published);
    }

    private static Module CommercialModule()
    {
        var module = Module.Create("wallet", "Wallet", "Wallet module").Value;
        module.MarkAsCommercial();
        module.SetAsAddon(15m, 150m);
        return module;
    }
}
