using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Common;
using TaxVision.Subscription.Domain.Seats;
using Wolverine;

namespace TaxVision.Subscription.Application.Seats.Commands.CancelSeatCheckout;

public static class CancelSeatCheckoutHandler
{
    public static async Task<Result<CancelCheckoutResponse>> Handle(
        CancelSeatCheckoutCommand command,
        ISeatPurchaseIntentRepository intents,
        IHostedCheckoutCancellationClient cancellation,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ISubscriptionAuditLogWriter audit,
        ILogger<SeatPurchaseIntent> logger,
        CancellationToken ct
    )
    {
        var intent = await intents.GetByIdAsync(command.IntentId, command.TenantId, ct);
        if (intent is null)
            return Result.Failure<CancelCheckoutResponse>(
                new Error("SeatPurchaseIntent.NotFound", "Seat purchase intent does not exist.")
            );

        if (
            intent.Status
            is SeatPurchaseIntentStatus.Cancelled
                or SeatPurchaseIntentStatus.Expired
                or SeatPurchaseIntentStatus.Failed
        )
            return Result.Success(new CancelCheckoutResponse(AlreadyCancelled: true, intent.Id));

        var nowUtc = DateTime.UtcNow;
        var previousStatus = intent.Status;

        var cancelled = intent.Cancel(command.ActorUserId, nowUtc, command.Reason);
        if (cancelled.IsFailure)
            return Result.Failure<CancelCheckoutResponse>(cancelled.Error);

        await CancelOnProviderAsync(intent, cancellation, logger, ct);
        await unitOfWork.SaveChangesAsync(ct);

        await bus.PublishAsync(
            new SeatCheckoutCancelledIntegrationEvent
            {
                TenantId = command.TenantId,
                SeatPurchaseIntentId = intent.Id,
                SaaSPaymentId = intent.SaaSPaymentId,
                ActorUserId = command.ActorUserId,
                Reason = command.Reason,
                CorrelationId = correlation.CorrelationId,
            }
        );

        await AuditEntryFactory.AppendAsync(
            audit,
            command.TenantId,
            "SeatPurchaseIntent",
            intent.Id,
            "SeatPurchaseIntent.Cancelled",
            command.ActorUserId,
            correlation.CorrelationId,
            before: new { Status = previousStatus.ToString() },
            after: new { Status = intent.Status.ToString() },
            reason: command.Reason,
            nowUtc,
            ct
        );

        logger.LogInformation(
            "Seat purchase intent {IntentId} cancelled by user {UserId} on tenant {TenantId}.",
            intent.Id,
            command.ActorUserId,
            command.TenantId
        );
        return Result.Success(new CancelCheckoutResponse(AlreadyCancelled: false, intent.Id));
    }

    private static async Task CancelOnProviderAsync(
        SeatPurchaseIntent intent,
        IHostedCheckoutCancellationClient cancellation,
        ILogger logger,
        CancellationToken ct
    )
    {
        if (intent.SaaSPaymentId is not { } paymentId)
            return;

        var result = await cancellation.CancelAsync(intent.TenantId, paymentId, ct);
        if (result.IsFailure)
            logger.LogWarning(
                "Provider cancel for seat intent {IntentId} returned failure: {Error}; proceeding — local state is Cancelled.",
                intent.Id,
                result.Error.Code
            );
    }
}
