using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.SubscriptionModules.Dtos;
using TaxVision.Subscription.Domain.Subscriptions;

namespace TaxVision.Subscription.Application.SubscriptionModules.Commands;

public record AssignSubscriptionModuleCommand(Guid SubscriptionId, Guid ModuleId, bool IsIncluded = true);
public record RemoveSubscriptionModuleCommand(Guid SubscriptionModuleId);

public static class AssignSubscriptionModuleHandler
{
    public static async Task<SubscriptionModuleDto> Handle(
        AssignSubscriptionModuleCommand cmd,
        ISubscriptionRepository subscriptionRepo,
        IModuleRepository moduleRepo,
        ISubscriptionModuleRepository subscriptionModuleRepo,
        IUnitOfWork uow,
        ILogger<AssignSubscriptionModuleCommand> logger,
        CancellationToken ct)
    {
        if (!await subscriptionRepo.ExistsAsync(cmd.SubscriptionId, ct))
            throw new InvalidOperationException($"Subscription {cmd.SubscriptionId} not found.");

        var module = await moduleRepo.GetByIdAsync(cmd.ModuleId, ct)
            ?? throw new InvalidOperationException($"Module {cmd.ModuleId} not found.");

        if (!module.IsActive)
            throw new InvalidOperationException($"Module {cmd.ModuleId} is not active.");

        var existing = await subscriptionModuleRepo.GetBySubscriptionAndModuleAsync(cmd.SubscriptionId, cmd.ModuleId, ct);
        if (existing != null)
            throw new InvalidOperationException("Module is already assigned to this subscription.");

        var subscriptionModule = SubscriptionModule.Create(cmd.SubscriptionId, cmd.ModuleId, cmd.IsIncluded);
        await subscriptionModuleRepo.AddAsync(subscriptionModule, ct);
        await uow.SaveChangesAsync(ct);

        logger.LogInformation("SubscriptionModule assigned: Subscription {SubId}, Module {ModId}",
            cmd.SubscriptionId, cmd.ModuleId);

        return new SubscriptionModuleDto
        {
            Id = subscriptionModule.Id,
            SubscriptionId = subscriptionModule.SubscriptionId,
            ModuleId = subscriptionModule.ModuleId,
            IsIncluded = subscriptionModule.IsIncluded,
            ModuleName = module.Name,
            ModuleDescription = module.Description,
            ModuleUrl = module.Url
        };
    }
}

public static class RemoveSubscriptionModuleHandler
{
    public static async Task<bool> Handle(
        RemoveSubscriptionModuleCommand cmd,
        ISubscriptionModuleRepository repo,
        IUnitOfWork uow,
        ILogger<RemoveSubscriptionModuleCommand> logger,
        CancellationToken ct)
    {
        var subscriptionModule = await repo.GetByIdAsync(cmd.SubscriptionModuleId, ct)
            ?? throw new InvalidOperationException($"SubscriptionModule {cmd.SubscriptionModuleId} not found.");

        repo.Remove(subscriptionModule);
        await uow.SaveChangesAsync(ct);

        logger.LogInformation("SubscriptionModule removed: {Id}", cmd.SubscriptionModuleId);
        return true;
    }
}
