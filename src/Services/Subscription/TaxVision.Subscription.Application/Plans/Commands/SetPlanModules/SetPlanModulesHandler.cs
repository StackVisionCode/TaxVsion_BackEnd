using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Entitlements.Commands.RecalculateEntitlementsForPlan;
using TaxVision.Subscription.Application.Plans;
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
        IModuleRepository modules,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ILogger<SubscriptionPlan> logger,
        CancellationToken ct
    )
    {
        var plan = await plans.GetByIdForUpdateAsync(command.PlanId, ct);
        if (plan is null)
            return Result.Failure(new Error("Plan.NotFound", "Plan not found."));

        var selection = await PlanModuleSelection.ComposeAsync(command.Modules, modules, ct);
        if (selection.IsFailure)
            return Result.Failure(selection.Error);

        var revised = plan.ReviseModules(selection.Value, command.ActorUserId, DateTime.UtcNow);
        if (revised.IsFailure)
            return revised;

        await unitOfWork.SaveChangesAsync(ct);
        await bus.PublishAsync(new RecalculateEntitlementsForPlanCommand(command.PlanId));
        await bus.PublishAsync(
            new CatalogChangedIntegrationEvent
            {
                ChangeKind = "plan.modules",
                EntityId = plan.Id,
                EntityCode = plan.Code.Value,
                CorrelationId = string.Empty,
            }
        );

        logger.LogInformation(
            "Plan {PlanId} modules revised by {ActorUserId}; mass recalculation queued for its tenants.",
            command.PlanId,
            command.ActorUserId
        );
        return Result.Success();
    }
}
