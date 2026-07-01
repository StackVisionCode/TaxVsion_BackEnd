using BuildingBlocks.Persistence;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;

namespace TaxVision.Subscription.Application.Plans.Commands;

public record DeletePlanCommand(Guid PlanId);

public static class DeletePlanHandler
{
    public static async Task<bool> Handle(
        DeletePlanCommand cmd,
        IPlanRepository repo,
        IUnitOfWork uow,
        ILogger<DeletePlanCommand> logger,
        CancellationToken ct)
    {
        var plan = await repo.GetByIdAsync(cmd.PlanId, ct)
            ?? throw new InvalidOperationException($"Plan {cmd.PlanId} not found.");

        var activeSubsCount = await repo.CountSubscriptionsAsync(cmd.PlanId, ct);

        if (activeSubsCount > 0)
        {
            plan.Deactivate();
            logger.LogInformation("Plan soft-deleted (used by {Count} subscriptions): {PlanId}", activeSubsCount, cmd.PlanId);
        }
        else
        {
            repo.Remove(plan);
            logger.LogInformation("Plan hard-deleted: {PlanId}", cmd.PlanId);
        }

        await uow.SaveChangesAsync(ct);
        return true;
    }
}
