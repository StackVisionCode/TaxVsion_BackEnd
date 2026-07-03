using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.SubscriptionModules.Dtos;
using TaxVision.Subscription.Domain.Subscriptions;

namespace TaxVision.Subscription.Application.SubscriptionModules.Commands;

public sealed record AssignSubscriptionModuleCommand(Guid SubscriptionId, Guid ModuleId, bool IsIncluded = true);
public sealed record RemoveSubscriptionModuleCommand(Guid SubscriptionModuleId);

public static class AssignSubscriptionModuleHandler
{
    public static async Task<Result<SubscriptionModuleDto>> Handle(
        AssignSubscriptionModuleCommand cmd,
        ISubscriptionRepository subscriptionRepo,
        IModuleRepository moduleRepo,
        ISubscriptionModuleRepository subscriptionModuleRepo,
        IUnitOfWork uow,
        ILogger<AssignSubscriptionModuleCommand> logger,
        CancellationToken ct)
    {
        if (!await subscriptionRepo.ExistsAsync(cmd.SubscriptionId, ct))
            return Result.Failure<SubscriptionModuleDto>(
                new Error("Subscription.NotFound", $"Subscription {cmd.SubscriptionId} not found."));

        var module = await moduleRepo.GetByIdAsync(cmd.ModuleId, ct);
        if (module is null)
            return Result.Failure<SubscriptionModuleDto>(
                new Error("Module.NotFound", $"Module {cmd.ModuleId} not found."));

        if (!module.IsActive)
            return Result.Failure<SubscriptionModuleDto>(
                new Error("Module.Inactive", $"Module {cmd.ModuleId} is not active."));

        var existing = await subscriptionModuleRepo.GetBySubscriptionAndModuleAsync(cmd.SubscriptionId, cmd.ModuleId, ct);
        if (existing is not null)
            return Result.Failure<SubscriptionModuleDto>(
                new Error("SubscriptionModule.AlreadyExists", "Module is already assigned to this subscription."));

        var subscriptionModule = SubscriptionModule.Create(cmd.SubscriptionId, cmd.ModuleId, cmd.IsIncluded);
        await subscriptionModuleRepo.AddAsync(subscriptionModule, ct);
        await uow.SaveChangesAsync(ct);

        logger.LogInformation("SubscriptionModule assigned: Subscription {SubId}, Module {ModId}", cmd.SubscriptionId, cmd.ModuleId);

        return Result.Success(new SubscriptionModuleDto
        {
            Id             = subscriptionModule.Id,
            SubscriptionId = subscriptionModule.SubscriptionId,
            ModuleId       = subscriptionModule.ModuleId,
            IsIncluded     = subscriptionModule.IsIncluded,
            ModuleName        = module.Name,
            ModuleDescription = module.Description,
            ModuleUrl         = module.Url
        });
    }
}

public static class RemoveSubscriptionModuleHandler
{
    public static async Task<Result> Handle(
        RemoveSubscriptionModuleCommand cmd,
        ISubscriptionModuleRepository repo,
        IUnitOfWork uow,
        ILogger<RemoveSubscriptionModuleCommand> logger,
        CancellationToken ct)
    {
        var subscriptionModule = await repo.GetByIdAsync(cmd.SubscriptionModuleId, ct);
        if (subscriptionModule is null)
            return Result.Failure(new Error("SubscriptionModule.NotFound",
                $"SubscriptionModule {cmd.SubscriptionModuleId} not found."));

        repo.Remove(subscriptionModule);
        await uow.SaveChangesAsync(ct);

        logger.LogInformation("SubscriptionModule removed: {Id}", cmd.SubscriptionModuleId);
        return Result.Success();
    }
}
