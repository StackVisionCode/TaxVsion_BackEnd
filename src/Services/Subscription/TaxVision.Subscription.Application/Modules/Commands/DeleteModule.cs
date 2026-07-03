using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;

namespace TaxVision.Subscription.Application.Modules.Commands;

public sealed record DeleteModuleCommand(Guid ModuleId);

public static class DeleteModuleHandler
{
    public static async Task<Result> Handle(
        DeleteModuleCommand cmd,
        IModuleRepository repo,
        IUnitOfWork uow,
        ILogger<DeleteModuleCommand> logger,
        CancellationToken ct)
    {
        var module = await repo.GetByIdAsync(cmd.ModuleId, ct);
        if (module is null)
            return Result.Failure(new Error("Module.NotFound", $"Module {cmd.ModuleId} not found."));

        var activeAssignments = await repo.CountActiveSubscriptionAssignmentsAsync(cmd.ModuleId, ct);
        if (activeAssignments > 0)
        {
            module.SoftDelete();
            logger.LogInformation("Module soft-deleted (used by {Count} assignments): {ModuleId}", activeAssignments, cmd.ModuleId);
        }
        else
        {
            repo.Remove(module);
            logger.LogInformation("Module hard-deleted: {ModuleId}", cmd.ModuleId);
        }

        await uow.SaveChangesAsync(ct);
        return Result.Success();
    }
}
