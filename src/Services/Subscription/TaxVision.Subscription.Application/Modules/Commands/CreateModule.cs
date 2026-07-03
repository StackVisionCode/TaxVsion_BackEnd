using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Modules.Dtos;
using TaxVision.Subscription.Domain.Modules;

namespace TaxVision.Subscription.Application.Modules.Commands;

public sealed record CreateModuleCommand(string Name, string Description, string? Url, bool IsActive);

public static class CreateModuleHandler
{
    public static async Task<Result<ModuleDto>> Handle(
        CreateModuleCommand cmd,
        IModuleRepository repo,
        IModuleReadService readService,
        IUnitOfWork uow,
        ILogger<CreateModuleCommand> logger,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cmd.Name))
            return Result.Failure<ModuleDto>(new Error("Module.NameRequired", "Name is required."));

        if (string.IsNullOrWhiteSpace(cmd.Description))
            return Result.Failure<ModuleDto>(new Error("Module.DescriptionRequired", "Description is required."));

        if (await repo.ExistsWithNameAsync(cmd.Name, null, ct))
            return Result.Failure<ModuleDto>(new Error("Module.NameConflict", $"Module name '{cmd.Name}' already exists."));

        var createResult = Module.Create(cmd.Name, cmd.Description, cmd.Url, cmd.IsActive);
        if (createResult.IsFailure)
            return Result.Failure<ModuleDto>(createResult.Error);

        var module = createResult.Value;
        await repo.AddAsync(module, ct);
        await uow.SaveChangesAsync(ct);

        logger.LogInformation("Module created: {ModuleId} ({Name})", module.Id, module.Name);
        return await readService.GetByIdWithDetailsAsync(module.Id, ct);
    }
}
