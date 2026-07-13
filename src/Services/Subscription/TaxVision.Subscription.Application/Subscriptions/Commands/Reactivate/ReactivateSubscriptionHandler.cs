using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Common;
using TaxVision.Subscription.Application.Entitlements.Commands.RecalculateEntitlements;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.Subscription.Application.Subscriptions.Commands.Reactivate;

public static class ReactivateSubscriptionHandler
{
    public static async Task<Result> Handle(
        ReactivateSubscriptionCommand command,
        ISubscriptionRepository subscriptions,
        IPlanRepository plans,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ISubscriptionAuditLogWriter audit,
        ILogger<TenantSubscription> logger,
        CancellationToken ct
    )
    {
        var subscription = await subscriptions.GetByTenantIdAsync(command.TenantId, ct);
        if (subscription is null)
            return Result.Failure(new Error("Subscription.NotFound", "Subscription does not exist."));

        var plan = await plans.GetByIdAsync(subscription.PlanId, ct);
        if (plan is null)
            return Result.Failure(new Error("Plan.NotFound", "Plan does not exist."));

        var planVersion = plan.GetPublishedVersion();
        if (planVersion is null)
            return Result.Failure(new Error("Plan.NoPublishedVersion", "Plan has no published version."));

        var nowUtc = DateTime.UtcNow;
        var periodEndUtc = subscription.BillingCycle.CalculateNext(nowUtc);
        var previousStatus = subscription.Status;

        var result = subscription.ReactivateAfterAdminReview(nowUtc, periodEndUtc, command.RequestedByUserId, nowUtc);
        if (result.IsFailure)
            return result;

        await unitOfWork.SaveChangesAsync(ct);

        await AuditEntryFactory.AppendAsync(
            audit, command.TenantId, "TenantSubscription", subscription.Id, "TenantSubscription.Reactivated",
            command.RequestedByUserId, correlation.CorrelationId,
            before: new { Status = previousStatus.ToString() },
            after: new { Status = subscription.Status.ToString() },
            reason: null, nowUtc, ct);

        await bus.InvokeAsync<Result>(new RecalculateEntitlementsCommand(command.TenantId), ct);

        logger.LogInformation("Subscription reactivated for tenant {TenantId}.", command.TenantId);
        return Result.Success();
    }
}
