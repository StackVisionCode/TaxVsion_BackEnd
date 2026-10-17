using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using TaxVision.Subscription.Application.Catalog.Commands.CreateModule;
using TaxVision.Subscription.Domain.Catalog;
using TaxVision.Subscription.Infrastructure.Catalog.Concurrency;
using TaxVision.Subscription.Tests.TestDoubles;
using Xunit;

namespace TaxVision.Subscription.Tests.Application;

public sealed class CreateModuleHandlerTests
{
    [Fact]
    public async Task Creates_a_core_module()
    {
        var modules = new FakeModuleRepo();
        var bus = new CapturingMessageBus();

        var result = await CreateModuleHandler.Handle(
            Command("Core", null, null),
            modules,
            new InMemoryModuleEditSerializer(),
            new FakeUnitOfWork(),
            bus,
            new FakeCorrelationContext(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        var module = Assert.Single(await modules.GetAllAsync());
        Assert.Equal(ModuleCategory.Core, module.Category);
        var changed = Assert.Single(bus.Published.OfType<ModuleCategoryChangedIntegrationEvent>());
        Assert.Equal("None", changed.OldCategory);
        Assert.False(changed.IsSellableAsAddon);
    }

    [Fact]
    public async Task Creates_a_sellable_commercial_module()
    {
        var modules = new FakeModuleRepo();

        var result = await CreateModuleHandler.Handle(
            Command("Commercial", 15m, 150m),
            modules,
            new InMemoryModuleEditSerializer(),
            new FakeUnitOfWork(),
            new CapturingMessageBus(),
            new FakeCorrelationContext(),
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        var module = Assert.Single(await modules.GetAllAsync());
        Assert.True(module.IsSellableAsAddon);
    }

    [Fact]
    public async Task Rejects_duplicate_code_case_insensitively()
    {
        var existing = Module.Create("wallet", "Wallet", "Wallet module").Value;
        var unitOfWork = new FakeUnitOfWork();

        var result = await CreateModuleHandler.Handle(
            Command("Core", null, null) with
            {
                Code = "WALLET",
            },
            new FakeModuleRepo(existing),
            new InMemoryModuleEditSerializer(),
            unitOfWork,
            new CapturingMessageBus(),
            new FakeCorrelationContext(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Module.CodeAlreadyExists", result.Error.Code);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Rejects_unknown_category()
    {
        var result = await CreateModuleHandler.Handle(
            Command("Legacy", null, null),
            new FakeModuleRepo(),
            new InMemoryModuleEditSerializer(),
            new FakeUnitOfWork(),
            new CapturingMessageBus(),
            new FakeCorrelationContext(),
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Module.InvalidCategory", result.Error.Code);
    }

    private static CreateModuleCommand Command(string category, decimal? monthly, decimal? yearly) =>
        new("wallet", "Wallet", "Wallet module", category, monthly, yearly, Guid.NewGuid());
}
