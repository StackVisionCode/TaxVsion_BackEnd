using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Modules.Dtos;
using TaxVision.Subscription.Domain.Modules;

namespace TaxVision.Subscription.Application.Modules.Commands;

public record CreateModuleCommand(string Name, string Description, string? Url, bool IsActive);

public static class CreateModuleHandler
{
    public static async Task<ModuleDto> Handle(
        CreateModuleCommand cmd,
        IModuleRepository repo,
        IModuleReadService readService,
        IUnitOfWork uow,
        ILogger<CreateModuleCommand> logger,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(cmd.Name))
            throw new ArgumentException("Name is required.");
        if (string.IsNullOrWhiteSpace(cmd.Description))
            throw new ArgumentException("Description is required.");

        if (await repo.ExistsWithNameAsync(cmd.Name, null, ct))
            throw new InvalidOperationException($"Module name '{cmd.Name}' already exists.");

        var module = Module.Create(cmd.Name, cmd.Description, cmd.Url, cmd.IsActive);
        await repo.AddAsync(module, ct);
        await uow.SaveChangesAsync(ct);

        logger.LogInformation("Module created: {ModuleId} ({Name})", module.Id, module.Name);
        return await readService.GetByIdWithDetailsAsync(module.Id, ct);
    }
}
