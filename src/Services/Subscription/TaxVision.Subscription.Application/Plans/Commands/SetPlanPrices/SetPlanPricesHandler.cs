using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;
using Wolverine;

namespace TaxVision.Subscription.Application.Plans.Commands.SetPlanPrices;

public static class SetPlanPricesHandler
{
    // Cambiar el precio no altera entitlements, así que a diferencia de SetPlanModules no encola recálculo.
    // Sí emite CatalogChangedIntegrationEvent (CAT-F5.2) para que los frontends que miran precios refetcheen.
    public static async Task<Result> Handle(
        SetPlanPricesCommand command,
        IPlanRepository plans,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var plan = await plans.GetByIdForUpdateAsync(command.PlanId, ct);
        if (plan is null)
            return Result.Failure(new Error("Plan.NotFound", "Plan not found."));

        var revised = plan.RevisePrices(command.MonthlyUsd, command.YearlyUsd, command.ActorUserId, DateTime.UtcNow);
        if (revised.IsFailure)
            return revised;

        await unitOfWork.SaveChangesAsync(ct);
        await bus.PublishAsync(
            new CatalogChangedIntegrationEvent
            {
                ChangeKind = "plan.prices",
                EntityId = plan.Id,
                EntityCode = plan.Code.Value,
                CorrelationId = string.Empty,
            }
        );
        return Result.Success();
    }
}
