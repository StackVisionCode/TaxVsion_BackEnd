using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Common;
using TaxVision.Subscription.Domain.Subscriptions;

namespace TaxVision.Subscription.Application.Subscriptions.Commands.CancelPendingPlanChange;

/// <summary>Cancela un downgrade agendado (nunca hay nada que cancelar de un upgrade — un
/// upgrade AwaitingPayment se resuelve solo, vía éxito o fallo del cobro).</summary>
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

        var pending = subscription.PendingDowngrades.FirstOrDefault(d => d.Status == PendingDowngradeStatus.Scheduled);
        if (pending is null)
            return Result.Failure(new Error("PendingDowngrade.NotFound", "There is no pending downgrade to cancel."));

        var nowUtc = DateTime.UtcNow;
        var result = subscription.CancelPendingDowngrade(pending.Id, command.RequestedByUserId, nowUtc);
        if (result.IsFailure)
            return result;

        await unitOfWork.SaveChangesAsync(ct);

        await AuditEntryFactory.AppendAsync(
            audit, command.TenantId, "TenantSubscription", subscription.Id, "TenantSubscription.PlanDowngradeCancelled",
            command.RequestedByUserId, correlation.CorrelationId,
            before: new { PendingPlanCode = pending.ToPlanCode },
            after: new { PendingPlanCode = (string?)null },
            reason: null,
            nowUtc,
            ct
        );

        logger.LogInformation(
            "Tenant {TenantId} cancelled its pending downgrade to {PlanCode} (requested by {UserId}).",
            command.TenantId,
            pending.ToPlanCode,
            command.RequestedByUserId
        );
        return Result.Success();
    }
}
