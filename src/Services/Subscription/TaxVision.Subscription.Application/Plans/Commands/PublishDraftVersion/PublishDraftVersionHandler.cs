using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Entitlements.Commands.RecalculateEntitlementsForPlan;
using TaxVision.Subscription.Domain.Plans;
using Wolverine;

namespace TaxVision.Subscription.Application.Plans.Commands.PublishDraftVersion;

public static class PublishDraftVersionHandler
{
    public static async Task<Result> Handle(
        PublishDraftVersionCommand command,
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

        var nowUtc = DateTime.UtcNow;
        var publishResult = plan.PublishVersion(command.VersionId, nowUtc, command.ActorUserId, nowUtc);
        if (publishResult.IsFailure)
            return publishResult;

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
