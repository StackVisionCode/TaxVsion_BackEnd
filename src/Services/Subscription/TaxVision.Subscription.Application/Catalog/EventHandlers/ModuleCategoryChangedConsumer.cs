using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;

namespace TaxVision.Subscription.Application.Catalog.EventHandlers;

public static class ModuleCategoryChangedConsumer
{
    public static Task Handle(
        ModuleCategoryChangedIntegrationEvent message,
        ICommercialModuleShadowReconciler reconciler,
        CancellationToken ct
    ) =>
        message.IsSellableAsAddon
            ? reconciler.UpsertShadowForAsync(message.ModuleCode, ct)
            : reconciler.DeprecateShadowForAsync(message.ModuleCode, ct);
}
