using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Catalog.Audit;
using TaxVision.Subscription.Domain.Catalog;
using TaxVision.Subscription.Domain.Plans;
using Wolverine;

namespace TaxVision.Subscription.Application.Plans.Commands.UpdatePlanMetadata;

public static class UpdatePlanMetadataHandler
{
    public static async Task<Result> Handle(
        UpdatePlanMetadataCommand command,
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

        var updated = plan.UpdateMetadata(command.Name, command.Description, command.ActorUserId, DateTime.UtcNow);
        if (updated.IsFailure)
            return updated;

        await CatalogAudit.RecordIfChangedAsync(
            audit,
            CatalogAuditActor.ForUser(command.ActorUserId),
            "plan.metadata",
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
                ChangeKind = "plan.metadata",
                EntityId = plan.Id,
                EntityCode = plan.Code.Value,
                CorrelationId = string.Empty,
            }
        );
        return Result.Success();
    }
}
