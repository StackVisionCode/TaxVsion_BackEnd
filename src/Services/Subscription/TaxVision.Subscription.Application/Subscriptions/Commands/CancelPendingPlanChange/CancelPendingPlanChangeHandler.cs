using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Common;
using TaxVision.Subscription.Domain.Subscriptions;

namespace TaxVision.Subscription.Application.Subscriptions.Commands.CancelPendingPlanChange;

public static class CancelPendingPlanChangeHandler
{
    public static async Task<Result> Handle(
        CancelPendingPlanChangeCommand command,
        ISubscriptionRepository subscriptions,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ISubscriptionAuditLogWriter audit,
        ILogger<TenantSubscription> logger,
        CancellationToken ct
    )
    {
        var subscription = await subscriptions.GetByTenantIdAsync(command.TenantId, ct);
        if (subscription is null)
            return Result.Failure(new Error("Subscription.NotFound", "Subscription does not exist."));

        var pending = subscription.PlanChangeRequests.FirstOrDefault(r => r.Status == PlanChangeRequestStatus.Pending);
        if (pending is null)
            return Result.Failure(new Error("PlanChangeRequest.NotFound", "There is no pending plan change to cancel."));

        var nowUtc = DateTime.UtcNow;
        var result = subscription.CancelPendingPlanChange(pending.Id, command.RequestedByUserId, nowUtc);
        if (result.IsFailure)
            return result;

        await unitOfWork.SaveChangesAsync(ct);

        await AuditEntryFactory.AppendAsync(
            audit, command.TenantId, "TenantSubscription", subscription.Id, "TenantSubscription.PlanChangeCancelled",
            command.RequestedByUserId, correlation.CorrelationId,
            before: new { PendingPlanCode = pending.ToPlanCode },
            after: new { PendingPlanCode = (string?)null },
            reason: null, nowUtc, ct);

        logger.LogInformation(
            "Tenant {TenantId} cancelled its pending plan change to {PlanCode} (requested by {UserId}).",
            command.TenantId,
            pending.ToPlanCode,
            command.RequestedByUserId
        );
        return Result.Success();
    }
}
