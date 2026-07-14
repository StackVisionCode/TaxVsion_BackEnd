using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Common;
using TaxVision.Subscription.Application.Entitlements.Commands.RecalculateEntitlements;
using TaxVision.Subscription.Domain.Subscriptions;
using Wolverine;

namespace TaxVision.Subscription.Application.Subscriptions.Commands.Cancel;

public static class CancelSubscriptionHandler
{
    public static async Task<Result> Handle(
        CancelSubscriptionCommand command,
        ISubscriptionRepository subscriptions,
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

        var nowUtc = DateTime.UtcNow;
        var previousStatus = subscription.Status;

        var result = subscription.CancelImmediately(command.Reason, command.RequestedByUserId, nowUtc);
        if (result.IsFailure)
            return result;

        await unitOfWork.SaveChangesAsync(ct);

        await AuditEntryFactory.AppendAsync(
            audit,
            command.TenantId,
            "TenantSubscription",
            subscription.Id,
            "TenantSubscription.Cancelled",
            command.RequestedByUserId,
            correlation.CorrelationId,
            before: new { Status = previousStatus.ToString() },
            after: new { Status = subscription.Status.ToString() },
            reason: command.Reason,
            nowUtc,
            ct
        );

        await bus.InvokeAsync<Result>(new RecalculateEntitlementsCommand(command.TenantId), ct);

        logger.LogInformation(
            "Tenant {TenantId} cancelled its subscription (requested by {UserId}): {Reason}.",
            command.TenantId,
            command.RequestedByUserId,
            command.Reason
        );
        return Result.Success();
    }
}
