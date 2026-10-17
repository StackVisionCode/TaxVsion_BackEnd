using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Catalog.Abstractions;
using TaxVision.Subscription.Domain.Catalog;
using Wolverine;

namespace TaxVision.Subscription.Application.Catalog.Commands.CreateModule;

public static class CreateModuleHandler
{
    public static async Task<Result<Guid>> Handle(
        CreateModuleCommand command,
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

        if (await modules.GetByCodeAsync(normalizedCode, ct) is not null)
            return Result.Failure<Guid>(
                new Error("Module.CodeAlreadyExists", $"Module '{normalizedCode}' already exists.")
            );

        var created = Module.Create(normalizedCode, command.Name, command.Description);
        if (created.IsFailure)
            return Result.Failure<Guid>(created.Error);

        var module = created.Value;
        var applied = ModuleCatalogRules.Apply(
            module,
            command.Category,
            command.AddonPriceMonthlyUsd,
            command.AddonPriceYearlyUsd
        );
        if (applied.IsFailure)
            return Result.Failure<Guid>(applied.Error);

        await modules.AddAsync(module, ct);
        await unitOfWork.SaveChangesAsync(ct);
        await bus.PublishAsync(
            new ModuleCategoryChangedIntegrationEvent
            {
                TenantId = PlatformTenant.Id,
                ModuleCode = module.Code,
                OldCategory = "None",
                NewCategory = module.Category.ToString(),
                IsSellableAsAddon = module.IsSellableAsAddon,
                CorrelationId = correlation.CorrelationId,
            }
        );
        await bus.PublishAsync(
            new CatalogChangedIntegrationEvent
            {
                TenantId = PlatformTenant.Id,
                ChangeKind = "module.created",
                EntityId = module.Id,
                EntityCode = module.Code,
                CorrelationId = correlation.CorrelationId,
            }
        );

        return Result.Success(module.Id);
    }
}
