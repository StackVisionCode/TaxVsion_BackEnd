using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Plans.Dtos;

namespace TaxVision.Subscription.Application.Plans.Commands;

public sealed record TogglePlanStatusCommand(Guid PlanId, bool IsActive);

public static class TogglePlanStatusHandler
{
    public static async Task<Result<PlanDto>> Handle(
        TogglePlanStatusCommand cmd,
        IPlanRepository repo,
        IPlanReadService readService,
        IUnitOfWork uow,
        ILogger<TogglePlanStatusCommand> logger,
        CancellationToken ct)
    {
        var plan = await repo.GetByIdAsync(cmd.PlanId, ct);
        if (plan is null)
            return Result.Failure<PlanDto>(new Error("Plan.NotFound", $"Plan {cmd.PlanId} not found."));

        var toggleResult = cmd.IsActive ? plan.Activate() : plan.Deactivate();
        if (toggleResult.IsFailure)
            return Result.Failure<PlanDto>(toggleResult.Error);

        await uow.SaveChangesAsync(ct);

        logger.LogInformation("Plan {Action}: {PlanId}", cmd.IsActive ? "activated" : "deactivated", cmd.PlanId);
        return await readService.GetByIdWithDetailsAsync(plan.Id, ct);
    }
}
