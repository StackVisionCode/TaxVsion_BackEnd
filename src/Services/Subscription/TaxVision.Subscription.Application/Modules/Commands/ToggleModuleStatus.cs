using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Modules.Dtos;

namespace TaxVision.Subscription.Application.Modules.Commands;

public record ToggleModuleStatusCommand(Guid ModuleId, bool IsActive);

public static class ToggleModuleStatusHandler
{
    public static async Task<ModuleDto> Handle(
        ToggleModuleStatusCommand cmd,
        IModuleRepository repo,
        IModuleReadService readService,
        IUnitOfWork uow,
        ILogger<ToggleModuleStatusCommand> logger,
        CancellationToken ct)
    {
        var module = await repo.GetByIdAsync(cmd.ModuleId, ct)
            ?? throw new InvalidOperationException($"Module {cmd.ModuleId} not found.");

        if (!cmd.IsActive && module.IsActive)
        {
            var affected = await repo.CountActiveSubscriptionsUsingAsync(cmd.ModuleId, ct);
            if (affected > 0)
                throw new InvalidOperationException(
                    $"Cannot deactivate module. {affected} active subscriptions are using it.");
        }

        if (cmd.IsActive)
            module.Activate();
        else
            module.Deactivate();

        await uow.SaveChangesAsync(ct);

        logger.LogInformation("Module {Action}: {ModuleId}", cmd.IsActive ? "activated" : "deactivated", cmd.ModuleId);
        return await readService.GetByIdWithDetailsAsync(module.Id, ct);
    }
}
