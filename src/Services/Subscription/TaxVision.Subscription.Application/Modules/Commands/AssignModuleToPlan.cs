using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Modules.Dtos;
using TaxVision.Subscription.Domain.Modules;

namespace TaxVision.Subscription.Application.Modules.Commands;

public record AssignModuleToPlanCommand(Guid ModuleId, Guid PlanId);
public record UnassignModuleFromPlanCommand(Guid ModuleId, Guid PlanId);

public static class AssignModuleToPlanHandler
{
    public static async Task<ModuleDto> Handle(
        AssignModuleToPlanCommand cmd,
        IModuleRepository moduleRepo,
        IPlanRepository planRepo,
        IModuleReadService readService,
        IUnitOfWork uow,
        ILogger<AssignModuleToPlanCommand> logger,
        CancellationToken ct)
    {
        if (!await moduleRepo.ExistsAsync(cmd.ModuleId, ct))
            throw new InvalidOperationException($"Module {cmd.ModuleId} not found.");
        if (!await planRepo.ExistsAsync(cmd.PlanId, ct))
            throw new InvalidOperationException($"Plan {cmd.PlanId} not found.");

        if (!await moduleRepo.PlanModuleLinkExistsAsync(cmd.ModuleId, cmd.PlanId, ct))
        {
            await moduleRepo.AddPlanModuleAsync(PlanModule.Create(cmd.PlanId, cmd.ModuleId), ct);
            await uow.SaveChangesAsync(ct);
        }

        logger.LogInformation("Module {ModuleId} assigned to Plan {PlanId}", cmd.ModuleId, cmd.PlanId);
        return await readService.GetByIdWithDetailsAsync(cmd.ModuleId, ct);
    }
}

public static class UnassignModuleFromPlanHandler
{
    public static async Task<bool> Handle(
        UnassignModuleFromPlanCommand cmd,
        IModuleRepository repo,
        IUnitOfWork uow,
        ILogger<UnassignModuleFromPlanCommand> logger,
        CancellationToken ct)
    {
        var link = await repo.GetPlanModuleLinkAsync(cmd.ModuleId, cmd.PlanId, ct)
            ?? throw new InvalidOperationException("Module is not assigned to this plan.");

        repo.RemovePlanModule(link);
        await uow.SaveChangesAsync(ct);

        logger.LogInformation("Module {ModuleId} unassigned from Plan {PlanId}", cmd.ModuleId, cmd.PlanId);
        return true;
    }
}
