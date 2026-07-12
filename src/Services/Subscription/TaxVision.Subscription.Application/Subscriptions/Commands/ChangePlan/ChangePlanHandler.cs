using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Common;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.Subscriptions;
using Wolverine;

namespace TaxVision.Subscription.Application.Subscriptions.Commands.ChangePlan;

public static class ChangePlanHandler
{
    public static async Task<Result> Handle(
        ChangePlanCommand command,
        ISubscriptionRepository subscriptions,
        IPlanRepository plans,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger<TenantSubscription> logger,
        CancellationToken ct
    )
    {
        var subscription = await subscriptions.GetByTenantIdAsync(command.TenantId, ct);
        if (subscription is null)
            return Result.Failure(new Error("Subscription.NotFound", "Subscription does not exist."));

        var plan = await plans.GetByCodeAsync(command.PlanCode?.Trim().ToLowerInvariant() ?? string.Empty, ct);
        if (plan is null || plan.Status != PlanStatus.Published)
            return Result.Failure(new Error("Plan.NotFound", "Plan does not exist."));

        var planVersion = plan.GetPublishedVersion();
        if (planVersion is null)
            return Result.Failure(new Error("Plan.NoPublishedVersion", "Plan has no published version."));

        if (subscription.PlanId == plan.Id && subscription.PlanVersionId == planVersion.Id)
            return Result.Success();

        var result = subscription.ChangePlan(plan, planVersion, command.RequestedByUserId, DateTime.UtcNow);
        if (result.IsFailure)
            return result;

        await bus.PublishAsync(SubscriptionEventFactory.PlanChanged(subscription, plan, planVersion, correlation.CorrelationId));
        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation(
            "Tenant {TenantId} changed plan to {PlanCode} (requested by {UserId}).",
            command.TenantId,
            plan.Code.Value,
            command.RequestedByUserId
        );
        return Result.Success();
    }
}
