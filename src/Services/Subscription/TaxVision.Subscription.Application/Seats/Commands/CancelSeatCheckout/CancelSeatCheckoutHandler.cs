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

        await unitOfWork.SaveChangesAsync(ct);

        if (intent.SaaSPaymentId is { } paymentId)
            await bus.PublishAsync(new CancelHostedCheckoutCommand(intent.TenantId, paymentId));

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
}
