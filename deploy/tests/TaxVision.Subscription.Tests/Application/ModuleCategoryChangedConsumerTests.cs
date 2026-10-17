using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using TaxVision.Subscription.Application.Catalog;
using TaxVision.Subscription.Application.Catalog.EventHandlers;
using Xunit;

namespace TaxVision.Subscription.Tests.Application;

public sealed class ModuleCategoryChangedConsumerTests
{
    [Theory]
    [InlineData(true, 1, 0)]
    [InlineData(false, 0, 1)]
    public async Task Reconciles_the_shadow_from_the_event(bool sellable, int upserts, int deprecations)
    {
        var reconciler = new CapturingReconciler();

        await ModuleCategoryChangedConsumer.Handle(
            new ModuleCategoryChangedIntegrationEvent
            {
                ModuleCode = "wallet",
                OldCategory = "Core",
                NewCategory = "Commercial",
                IsSellableAsAddon = sellable,
            },
            reconciler,
            CancellationToken.None
        );

        Assert.Equal(upserts, reconciler.Upserts);
        Assert.Equal(deprecations, reconciler.Deprecations);
    }

    private sealed class CapturingReconciler : ICommercialModuleShadowReconciler
    {
        public int Upserts { get; private set; }
        public int Deprecations { get; private set; }

        public Task ReconcileAllAsync(CancellationToken ct) => Task.CompletedTask;

        public Task UpsertShadowForAsync(string moduleCode, CancellationToken ct)
        {
            Upserts++;
            return Task.CompletedTask;
        }

        public Task DeprecateShadowForAsync(string moduleCode, CancellationToken ct)
        {
            Deprecations++;
            return Task.CompletedTask;
        }
    }
}
