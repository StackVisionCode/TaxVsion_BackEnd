using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Entitlements.Commands.RecalculateEntitlements;
using TaxVision.Subscription.Domain.Subscriptions;
using Wolverine;

namespace TaxVision.Subscription.Application.Subscriptions.Commands.Suspend;

public static class SuspendSubscriptionHandler
{
    public static async Task<Result> Handle(
        SuspendSubscriptionCommand command,
        ISubscriptionRepository subscriptions,
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

        var result = subscription.SuspendForPolicyViolation(command.Reason, command.RequestedByUserId, DateTime.UtcNow);
        if (result.IsFailure)
            return result;

        await bus.PublishAsync(
            new SubscriptionSuspendedIntegrationEvent
            {
                TenantId = subscription.TenantId,
                SubscribedTenantId = subscription.TenantId,
                Reason = command.Reason,
                CorrelationId = correlation.CorrelationId,
            }
        );
        await unitOfWork.SaveChangesAsync(ct);

        await bus.InvokeAsync<Result>(new RecalculateEntitlementsCommand(command.TenantId), ct);

        logger.LogWarning("Subscription suspended for tenant {TenantId}: {Reason}.", command.TenantId, command.Reason);
        return Result.Success();
    }
}
