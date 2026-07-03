using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Modules.Dtos;

namespace TaxVision.Subscription.Application.Modules.Commands;

public sealed record ToggleModuleStatusCommand(Guid ModuleId, bool IsActive);

public static class ToggleModuleStatusHandler
{
    public static async Task<Result<ModuleDto>> Handle(
        ToggleModuleStatusCommand cmd,
        IModuleRepository repo,
        IModuleReadService readService,
        IUnitOfWork uow,
        ILogger<ToggleModuleStatusCommand> logger,
        CancellationToken ct)
    {
        var module = await repo.GetByIdAsync(cmd.ModuleId, ct);
        if (module is null)
            return Result.Failure<ModuleDto>(new Error("Module.NotFound", $"Module {cmd.ModuleId} not found."));

        if (!cmd.IsActive && module.IsActive)
        {
            var affected = await repo.CountActiveSubscriptionsUsingAsync(cmd.ModuleId, ct);
            if (affected > 0)
                return Result.Failure<ModuleDto>(new Error("Module.InUse",
                    $"Cannot deactivate module. {affected} active subscriptions are using it."));
        }

        var toggleResult = cmd.IsActive ? module.Activate() : module.Deactivate();
        if (toggleResult.IsFailure)
            return Result.Failure<ModuleDto>(toggleResult.Error);
        await uow.SaveChangesAsync(ct);

        logger.LogInformation("Module {Action}: {ModuleId}", cmd.IsActive ? "activated" : "deactivated", cmd.ModuleId);
        return await readService.GetByIdWithDetailsAsync(module.Id, ct);
    }
}
