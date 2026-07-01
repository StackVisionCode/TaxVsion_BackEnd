using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Modules.Dtos;

namespace TaxVision.Subscription.Application.Modules.Commands;

public record UpdateModuleCommand(Guid Id, string Name, string Description, string? Url, bool IsActive);

public static class UpdateModuleHandler
{
    public static async Task<ModuleDto> Handle(
        UpdateModuleCommand cmd,
        IModuleRepository repo,
        IModuleReadService readService,
        IUnitOfWork uow,
        ILogger<UpdateModuleCommand> logger,
        CancellationToken ct)
    {
        var module = await repo.GetByIdAsync(cmd.Id, ct)
            ?? throw new InvalidOperationException($"Module {cmd.Id} not found.");

        if (await repo.ExistsWithNameAsync(cmd.Name, cmd.Id, ct))
            throw new InvalidOperationException($"Module name '{cmd.Name}' already exists.");

        module.Update(cmd.Name, cmd.Description, cmd.Url, cmd.IsActive);
        await uow.SaveChangesAsync(ct);

        logger.LogInformation("Module updated: {ModuleId}", module.Id);
        return await readService.GetByIdWithDetailsAsync(module.Id, ct);
    }
}
