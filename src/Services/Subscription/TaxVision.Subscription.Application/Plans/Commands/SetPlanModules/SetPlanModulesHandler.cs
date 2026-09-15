using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Entitlements.Commands.RecalculateEntitlementsForPlan;
using TaxVision.Subscription.Domain.Plans;
using Wolverine;

namespace TaxVision.Subscription.Application.Plans.Commands.SetPlanModules;

public static class SetPlanModulesHandler
{
    // Revisa los módulos del plan (nueva versión publicada, supersede la anterior), guarda, y encola
    // el recálculo masivo para propagar el cambio a todos los tenants del plan.
    public static async Task<Result> Handle(
        SetPlanModulesCommand command,
        IPlanRepository plans,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ILogger<SubscriptionPlan> logger,
        CancellationToken ct
    )
    {
        var plan = await plans.GetByIdForUpdateAsync(command.PlanId, ct);
        if (plan is null)
            return Result.Failure(new Error("Plan.NotFound", "Plan not found."));

        var revised = plan.ReviseModules(command.Modules, command.ActorUserId, DateTime.UtcNow);
        if (revised.IsFailure)
            return revised;

        await unitOfWork.SaveChangesAsync(ct);
        await bus.PublishAsync(new RecalculateEntitlementsForPlanCommand(command.PlanId));

        logger.LogInformation(
            "Plan {PlanId} modules revised by {ActorUserId}; mass recalculation queued for its tenants.",
            command.PlanId,
            command.ActorUserId
        );
        return Result.Success();
    }
}
