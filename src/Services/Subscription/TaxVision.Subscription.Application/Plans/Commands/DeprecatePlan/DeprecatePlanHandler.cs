using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;
using Wolverine;

namespace TaxVision.Subscription.Application.Plans.Commands.DeprecatePlan;

public static class DeprecatePlanHandler
{
    public static async Task<Result> Handle(
        DeprecatePlanCommand command,
        IPlanRepository plans,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var plan = await plans.GetByIdForUpdateAsync(command.PlanId, ct);
        if (plan is null)
            return Result.Failure(new Error("Plan.NotFound", "Plan not found."));

        var result = plan.Deprecate(command.ActorUserId, DateTime.UtcNow);
        if (result.IsFailure)
            return result;

        await unitOfWork.SaveChangesAsync(ct);
        await bus.PublishAsync(
            new CatalogChangedIntegrationEvent
            {
                ChangeKind = "plan.deprecated",
                EntityId = plan.Id,
                EntityCode = plan.Code.Value,
                CorrelationId = string.Empty,
            }
        );
        return Result.Success();
    }
}

public static class ArchivePlanHandler
{
    public static async Task<Result> Handle(
        ArchivePlanCommand command,
        IPlanRepository plans,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct
    )
    {
        var plan = await plans.GetByIdForUpdateAsync(command.PlanId, ct);
        if (plan is null)
            return Result.Failure(new Error("Plan.NotFound", "Plan not found."));

        var result = plan.Archive(command.ActorUserId, DateTime.UtcNow);
        if (result.IsFailure)
            return result;

        await unitOfWork.SaveChangesAsync(ct);
        await bus.PublishAsync(
            new CatalogChangedIntegrationEvent
            {
                ChangeKind = "plan.archived",
                EntityId = plan.Id,
                EntityCode = plan.Code.Value,
                CorrelationId = string.Empty,
            }
        );
        return Result.Success();
    }
}
