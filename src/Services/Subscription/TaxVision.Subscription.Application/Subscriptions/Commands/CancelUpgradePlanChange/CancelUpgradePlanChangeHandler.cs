using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Common;
using TaxVision.Subscription.Application.Seats.Commands.CancelSeatCheckout;
using TaxVision.Subscription.Domain.Subscriptions;
using Wolverine;

namespace TaxVision.Subscription.Application.Subscriptions.Commands.CancelUpgradePlanChange;

public static class CancelUpgradePlanChangeHandler
{
    public static async Task<Result<CancelCheckoutResponse>> Handle(
        CancelUpgradePlanChangeCommand command,
        ISubscriptionRepository subscriptions,
        IHostedCheckoutCancellationClient cancellation,
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
            return Result.Failure<CancelCheckoutResponse>(
                new Error("Subscription.NotFound", "Subscription does not exist.")
            );

        var nowUtc = DateTime.UtcNow;
        var cancelled = subscription.CancelPendingUpgrade(command.ActorUserId, nowUtc, command.Reason);
        if (cancelled.IsFailure)
            return Result.Failure<CancelCheckoutResponse>(cancelled.Error);

        if (cancelled.Value is null)
            return Result.Success(new CancelCheckoutResponse(AlreadyCancelled: true, Guid.Empty));

        var request = cancelled.Value;
        if (request.SaaSPaymentId is { } paymentId)
        {
            var providerResult = await cancellation.CancelAsync(command.TenantId, paymentId, ct);
            if (providerResult.IsFailure)
                logger.LogWarning(
                    "Provider cancel for upgrade request {RequestId} returned failure: {Error}; proceeding — local state is Cancelled.",
                    request.Id,
                    providerResult.Error.Code
                );
        }

        await unitOfWork.SaveChangesAsync(ct);

        await bus.PublishAsync(
            new PlanChangeUpgradeCancelledIntegrationEvent
            {
                TenantId = command.TenantId,
                PlanChangeRequestId = request.Id,
                SaaSPaymentId = request.SaaSPaymentId,
                ActorUserId = command.ActorUserId,
                Reason = command.Reason,
                CorrelationId = correlation.CorrelationId,
            }
        );

        await AuditEntryFactory.AppendAsync(
            audit,
            command.TenantId,
            "PlanChangeRequest",
            request.Id,
            "PlanChangeRequest.UpgradeCancelled",
            command.ActorUserId,
            correlation.CorrelationId,
            before: new { Status = PlanChangeRequestStatus.AwaitingPayment.ToString() },
            after: new { Status = request.Status.ToString() },
            reason: command.Reason,
            nowUtc,
            ct
        );

        logger.LogInformation(
            "Pending upgrade {RequestId} cancelled by user {UserId} on tenant {TenantId}.",
            request.Id,
            command.ActorUserId,
            command.TenantId
        );
        return Result.Success(new CancelCheckoutResponse(AlreadyCancelled: false, request.Id));
    }
}
