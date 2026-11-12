using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Catalog.Audit;
using TaxVision.Subscription.Application.Entitlements.Commands.RecalculateEntitlementsForPlan;
using TaxVision.Subscription.Domain.Catalog;
using TaxVision.Subscription.Domain.Plans;
using Wolverine;

namespace TaxVision.Subscription.Application.Plans.Commands.PublishDraftVersion;

public static class PublishDraftVersionHandler
{
    public static async Task<Result> Handle(
        PublishDraftVersionCommand command,
        IPlanRepository plans,
        IUnitOfWork unitOfWork,
        ICatalogAuditLog audit,
        ICorrelationContext correlation,
        IMessageBus bus,
        ILogger<SubscriptionPlan> logger,
        CancellationToken ct
    )
    {
        var plan = await plans.GetByIdForUpdateAsync(command.PlanId, ct);
        if (plan is null)
            return Result.Failure(new Error("Plan.NotFound", "Plan not found."));

        var before = CatalogAuditViews.Of(plan);

        var nowUtc = DateTime.UtcNow;
        var publishResult = plan.PublishVersion(command.VersionId, nowUtc, command.ActorUserId, nowUtc);
        if (publishResult.IsFailure)
            return publishResult;

        await CatalogAudit.RecordIfChangedAsync(
            audit,
            CatalogAuditActor.ForUser(command.ActorUserId),
            "plan.version_published",
            CatalogAuditViews.Target(plan),
            before,
            CatalogAuditViews.Of(plan),
            reason: null,
            correlation.CorrelationId,
            DateTime.UtcNow,
            ct
        );
        await unitOfWork.SaveChangesAsync(ct);
        await bus.PublishAsync(new RecalculateEntitlementsForPlanCommand(command.PlanId));
        await bus.PublishAsync(
            new CatalogChangedIntegrationEvent
            {
                ChangeKind = "plan.version_published",
                EntityId = command.PlanId,
                EntityCode = plan.Code.Value,
                CorrelationId = string.Empty,
            }
        );

        logger.LogInformation(
            "Plan {PlanId} version {VersionId} published by {ActorUserId}; mass recalculation queued, catalog change broadcast.",
            command.PlanId,
            command.VersionId,
            command.ActorUserId
        );
        return Result.Success();
    }
}
