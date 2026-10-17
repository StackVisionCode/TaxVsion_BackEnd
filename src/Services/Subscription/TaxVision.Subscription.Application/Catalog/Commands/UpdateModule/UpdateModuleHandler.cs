using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Catalog.Abstractions;
using TaxVision.Subscription.Application.Entitlements.Commands.RecalculateEntitlementsForAllPlans;
using Wolverine;

namespace TaxVision.Subscription.Application.Catalog.Commands.UpdateModule;

public static class UpdateModuleHandler
{
    public static async Task<Result> Handle(
        UpdateModuleCommand command,
        IModuleRepository modules,
        IModuleEditSerializer serializer,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var normalizedCode = command.Code.Trim().ToLowerInvariant();
        await using var lease = await serializer.AcquireAsync(normalizedCode, ct);

        var module = await modules.GetByCodeForUpdateAsync(normalizedCode, ct);
        if (module is null)
            return Result.Failure(new Error("Module.NotFound", "Module does not exist."));

        var oldCategory = module.Category;
        var oldSellable = module.IsSellableAsAddon;
        var oldMonthlyPrice = module.AddonPriceMonthlyUsd;
        var oldYearlyPrice = module.AddonPriceYearlyUsd;

        var applied = ModuleCatalogRules.Apply(
            module,
            command.Category,
            command.AddonPriceMonthlyUsd,
            command.AddonPriceYearlyUsd
        );
        if (applied.IsFailure)
            return applied;

        var structureChanged = oldCategory != module.Category || oldSellable != module.IsSellableAsAddon;
        var priceChanged =
            oldMonthlyPrice != module.AddonPriceMonthlyUsd || oldYearlyPrice != module.AddonPriceYearlyUsd;
        if (!structureChanged && !priceChanged)
            return Result.Success();

        await unitOfWork.SaveChangesAsync(ct);
        await bus.PublishAsync(
            new ModuleCategoryChangedIntegrationEvent
            {
                TenantId = PlatformTenant.Id,
                ModuleCode = module.Code,
                OldCategory = oldCategory.ToString(),
                NewCategory = module.Category.ToString(),
                IsSellableAsAddon = module.IsSellableAsAddon,
                CorrelationId = correlation.CorrelationId,
            }
        );
        await bus.PublishAsync(
            new CatalogChangedIntegrationEvent
            {
                TenantId = PlatformTenant.Id,
                ChangeKind = "module.configuration",
                EntityId = module.Id,
                EntityCode = module.Code,
                CorrelationId = correlation.CorrelationId,
            }
        );

        if (structureChanged)
            await bus.PublishAsync(new RecalculateEntitlementsForAllPlansCommand());

        return Result.Success();
    }
}
