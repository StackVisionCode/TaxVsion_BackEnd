using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Common;
using TaxVision.Subscription.Application.Entitlements.Commands.RecalculateEntitlements;
using TaxVision.Subscription.Domain.Subscriptions;
using Wolverine;

namespace TaxVision.Subscription.Application.Subscriptions.Commands.Renew;

public static class RenewTenantSubscriptionHandler
{
    public static async Task<Result> Handle(
        RenewTenantSubscriptionCommand command,
        ISubscriptionRepository subscriptions,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ILogger<TenantSubscription> logger,
        CancellationToken ct
    )
    {
        var subscription = await subscriptions.GetByTenantIdAsync(command.TenantId, ct);
        if (subscription is null)
            return Result.Failure(new Error("Subscription.NotFound", "Subscription does not exist."));

        var completeResult = BeginAndCompleteRenewal(subscription, command.RequestedByUserId);
        if (completeResult.IsFailure)
            return completeResult;

        await unitOfWork.SaveChangesAsync(ct);
        await bus.InvokeAsync<Result>(new RecalculateEntitlementsCommand(command.TenantId), ct);

        logger.LogInformation(
            "Subscription manually renewed for tenant {TenantId} (requested by {UserId}).", command.TenantId, command.RequestedByUserId);
        return Result.Success();
    }

    private static Result BeginAndCompleteRenewal(TenantSubscription subscription, Guid actorUserId)
    {
        var nowUtc = DateTime.UtcNow;
        var idempotencyKey = IdempotencyKeyFactory.SubscriptionRenewal(subscription.Id, subscription.CurrentPeriodEndUtc);

        var beginResult = subscription.BeginRenewal(idempotencyKey, actorUserId, nowUtc);
        if (beginResult.IsFailure)
            return beginResult;

        var renewal = FindRenewalByKey(subscription, idempotencyKey);
        if (renewal is null)
            return Result.Failure(new Error("Subscription.RenewalNotFound", "Renewal was not scheduled."));

        return subscription.CompleteRenewal(renewal.Id, externalPaymentReference: "manual-admin-renewal", actorUserId, nowUtc);
    }

    private static TenantSubscriptionRenewal? FindRenewalByKey(TenantSubscription subscription, string idempotencyKey)
    {
        foreach (var renewal in subscription.Renewals)
        {
            if (renewal.IdempotencyKey == idempotencyKey)
                return renewal;
        }

        return null;
    }
}
