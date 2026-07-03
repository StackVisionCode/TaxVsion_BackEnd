using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Modules.Dtos;

namespace TaxVision.Subscription.Application.Modules.Commands;

public sealed record UpdateModuleCommand(Guid Id, string Name, string Description, string? Url, bool IsActive);

public static class UpdateModuleHandler
{
    public static async Task<Result<ModuleDto>> Handle(
        UpdateModuleCommand cmd,
        IModuleRepository repo,
        IModuleReadService readService,
        IUnitOfWork uow,
        ILogger<UpdateModuleCommand> logger,
        CancellationToken ct)
    {
        var module = await repo.GetByIdAsync(cmd.Id, ct);
        if (module is null)
            return Result.Failure<ModuleDto>(new Error("Module.NotFound", $"Module {cmd.Id} not found."));

        if (await repo.ExistsWithNameAsync(cmd.Name, cmd.Id, ct))
            return Result.Failure<ModuleDto>(new Error("Module.NameConflict", $"Module name '{cmd.Name}' already exists."));

        var updateResult = module.Update(cmd.Name, cmd.Description, cmd.Url, cmd.IsActive);
        if (updateResult.IsFailure)
            return Result.Failure<ModuleDto>(updateResult.Error);

        await uow.SaveChangesAsync(ct);

        logger.LogInformation("Module updated: {ModuleId}", module.Id);
        return await readService.GetByIdWithDetailsAsync(module.Id, ct);
    }
}
