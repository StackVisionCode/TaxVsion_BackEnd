using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Common;
using TaxVision.Subscription.Application.Entitlements.Commands.RecalculateEntitlements;
using TaxVision.Subscription.Application.Subscriptions.IntegrationEvents;
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

        var result = subscription.ScheduleCancellation(command.Reason, command.RequestedByUserId, nowUtc);
        if (result.IsFailure)
            return result;

        await unitOfWork.SaveChangesAsync(ct);

        await AuditEntryFactory.AppendAsync(
            audit,
            command.TenantId,
            "TenantSubscription",
            subscription.Id,
            "TenantSubscription.CancellationScheduled",
            command.RequestedByUserId,
            correlation.CorrelationId,
            before: new { Status = previousStatus.ToString(), CancelAtPeriodEnd = false },
            after: new
            {
                Status = subscription.Status.ToString(),
                CancelAtPeriodEnd = true,
                AccessEndsAtUtc = subscription.CurrentPeriodEndUtc,
            },
            reason: command.Reason,
            nowUtc,
            ct
        );

        // El acceso NO cambia acá: sigue activo y pagado hasta el fin del período, así que no hay
        // entitlements que recalcular. El job de renovación expira y recalcula cuando llegue la fecha.
        await bus.PublishStatusChangedAsync(
            subscription,
            previousStatus,
            SubscriptionChangeReason.CancellationScheduled,
            command.RequestedByUserId,
            correlationId: correlation.CorrelationId
        );

        logger.LogInformation(
            "Tenant {TenantId} scheduled its cancellation for {AccessEndsAtUtc} (requested by {UserId}): {Reason}.",
            command.TenantId,
            subscription.CurrentPeriodEndUtc,
            command.RequestedByUserId,
            command.Reason
        );
        return Result.Success();
    }
}
