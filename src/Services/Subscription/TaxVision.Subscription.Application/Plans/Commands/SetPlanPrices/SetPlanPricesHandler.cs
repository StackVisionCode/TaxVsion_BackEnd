using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Catalog.Audit;
using TaxVision.Subscription.Domain.Catalog;
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
        ICatalogAuditLog audit,
        ICorrelationContext correlation,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var plan = await plans.GetByIdForUpdateAsync(command.PlanId, ct);
        if (plan is null)
            return Result.Failure(new Error("Plan.NotFound", "Plan not found."));

        var before = CatalogAuditViews.Of(plan);

        var revised = plan.RevisePrices(command.MonthlyUsd, command.YearlyUsd, command.ActorUserId, DateTime.UtcNow);
        if (revised.IsFailure)
            return revised;

        await CatalogAudit.RecordIfChangedAsync(
            audit,
            CatalogAuditActor.ForUser(command.ActorUserId),
            "plan.prices",
            CatalogAuditViews.Target(plan),
            before,
            CatalogAuditViews.Of(plan),
            reason: null,
            correlation.CorrelationId,
            DateTime.UtcNow,
            ct
        );
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
