using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Domain.Plans;
using Wolverine;

namespace TaxVision.Subscription.Application.Plans.Commands.UpdatePlanMetadata;

public static class UpdatePlanMetadataHandler
{
    public static async Task<Result> Handle(
        UpdatePlanMetadataCommand command,
        IPlanRepository plans,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var plan = await plans.GetByIdForUpdateAsync(command.PlanId, ct);
        if (plan is null)
            return Result.Failure(new Error("Plan.NotFound", "Plan not found."));

        var updated = plan.UpdateMetadata(command.Name, command.Description, command.ActorUserId, DateTime.UtcNow);
        if (updated.IsFailure)
            return updated;

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
