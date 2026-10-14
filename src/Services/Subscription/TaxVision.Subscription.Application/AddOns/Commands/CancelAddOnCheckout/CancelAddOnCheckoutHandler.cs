using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Common;
using TaxVision.Subscription.Application.Seats.Commands.CancelSeatCheckout;
using TaxVision.Subscription.Domain.AddOns;
using Wolverine;

namespace TaxVision.Subscription.Application.AddOns.Commands.CancelAddOnCheckout;

public static class CancelAddOnCheckoutHandler
{
    public static async Task<Result<CancelCheckoutResponse>> Handle(
        CancelAddOnCheckoutCommand command,
        IAddOnPurchaseIntentRepository intents,
        IHostedCheckoutCancellationClient cancellation,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ISubscriptionAuditLogWriter audit,
        ILogger<AddOnPurchaseIntent> logger,
        CancellationToken ct
    )
    {
        var intent = await intents.GetByIdAsync(command.IntentId, command.TenantId, ct);
        if (intent is null)
            return Result.Failure<CancelCheckoutResponse>(
                new Error("AddOnPurchaseIntent.NotFound", "Add-on purchase intent does not exist.")
            );

        if (
            intent.Status
            is AddOnPurchaseIntentStatus.Cancelled
                or AddOnPurchaseIntentStatus.Expired
                or AddOnPurchaseIntentStatus.Failed
        )
            return Result.Success(new CancelCheckoutResponse(AlreadyCancelled: true, intent.Id));

        var nowUtc = DateTime.UtcNow;
        var previousStatus = intent.Status;

        var cancelled = intent.Cancel(command.ActorUserId, nowUtc, command.Reason);
        if (cancelled.IsFailure)
            return Result.Failure<CancelCheckoutResponse>(cancelled.Error);

        if (intent.SaaSPaymentId is { } paymentId)
        {
            var providerResult = await cancellation.CancelAsync(intent.TenantId, paymentId, ct);
            if (providerResult.IsFailure)
                logger.LogWarning(
                    "Provider cancel for add-on intent {IntentId} returned failure: {Error}; proceeding — local state is Cancelled.",
                    intent.Id,
                    providerResult.Error.Code
                );
        }

        await unitOfWork.SaveChangesAsync(ct);

        await bus.PublishAsync(
            new AddOnCheckoutCancelledIntegrationEvent
            {
                TenantId = command.TenantId,
                AddOnPurchaseIntentId = intent.Id,
                SaaSPaymentId = intent.SaaSPaymentId,
                ActorUserId = command.ActorUserId,
                Reason = command.Reason,
                CorrelationId = correlation.CorrelationId,
            }
        );

        await AuditEntryFactory.AppendAsync(
            audit,
            command.TenantId,
            "AddOnPurchaseIntent",
            intent.Id,
            "AddOnPurchaseIntent.Cancelled",
            command.ActorUserId,
            correlation.CorrelationId,
            before: new { Status = previousStatus.ToString() },
            after: new { Status = intent.Status.ToString() },
            reason: command.Reason,
            nowUtc,
            ct
        );

        logger.LogInformation(
            "Add-on purchase intent {IntentId} cancelled by user {UserId} on tenant {TenantId}.",
            intent.Id,
            command.ActorUserId,
            command.TenantId
        );
        return Result.Success(new CancelCheckoutResponse(AlreadyCancelled: false, intent.Id));
    }
}
