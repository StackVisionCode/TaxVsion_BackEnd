using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Plans.Dtos;

namespace TaxVision.Subscription.Application.Plans.Commands;

public record TogglePlanStatusCommand(Guid PlanId, bool IsActive);

public static class TogglePlanStatusHandler
{
    public static async Task<PlanDto> Handle(
        TogglePlanStatusCommand cmd,
        IPlanRepository repo,
        IPlanReadService readService,
        IUnitOfWork uow,
        ILogger<TogglePlanStatusCommand> logger,
        CancellationToken ct)
    {
        var plan = await repo.GetByIdAsync(cmd.PlanId, ct)
            ?? throw new InvalidOperationException($"Plan {cmd.PlanId} not found.");

        if (cmd.IsActive)
            plan.Activate();
        else
            plan.Deactivate();

        await uow.SaveChangesAsync(ct);

        logger.LogInformation("Plan {Action}: {PlanId}", cmd.IsActive ? "activated" : "deactivated", cmd.PlanId);
        return await readService.GetByIdWithDetailsAsync(plan.Id, ct);
    }
}
