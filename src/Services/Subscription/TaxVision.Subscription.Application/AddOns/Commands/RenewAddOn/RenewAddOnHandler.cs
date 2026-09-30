using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Common;
using TaxVision.Subscription.Application.Entitlements.Commands.RecalculateEntitlements;
using TaxVision.Subscription.Domain.AddOns;
using Wolverine;

namespace TaxVision.Subscription.Application.AddOns.Commands.RenewAddOn;

public static class RenewAddOnHandler
{
    public static async Task<Result> Handle(
        RenewAddOnCommand command,
        ITenantAddOnRepository tenantAddOns,
        ISubscriptionRepository subscriptions,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ILogger<TenantAddOn> logger,
        CancellationToken ct
    )
    {
        var addOn = await tenantAddOns.GetByIdAsync(command.TenantAddOnId, command.TenantId, ct);
        if (addOn is null)
            return Result.Failure(new Error("AddOn.NotFound", "Add-on does not exist."));

        var subscription = await subscriptions.GetByTenantIdAsync(command.TenantId, ct);
        if (subscription is null)
            return Result.Failure(new Error("Subscription.NotFound", "Subscription does not exist."));

        // Co-terminación: la renovación llega al próximo aniversario de la base.
        var result = BeginAndCompleteRenewal(
            addOn,
            subscription.NextCoTermEnd(addOn.CurrentPeriodEndUtc),
            command.RequestedByUserId
        );
        if (result.IsFailure)
            return result;

        await unitOfWork.SaveChangesAsync(ct);
        await bus.RecalculateEntitlementsSafelyAsync(command.TenantId, logger, ct);

        logger.LogInformation(
            "Add-on {TenantAddOnId} renewed without charge by platform support {UserId}.",
            addOn.Id,
            command.RequestedByUserId
        );
        return Result.Success();
    }

    private static Result BeginAndCompleteRenewal(TenantAddOn addOn, DateTime newPeriodEndUtc, Guid actorUserId)
    {
        var nowUtc = DateTime.UtcNow;
        var idempotencyKey = IdempotencyKeyFactory.AddOnRenewal(addOn.Id, addOn.CurrentPeriodEndUtc);

        var beginResult = addOn.BeginRenewal(idempotencyKey, newPeriodEndUtc, actorUserId, nowUtc);
        if (beginResult.IsFailure)
            return beginResult;

        var renewal = FindRenewalByKey(addOn, idempotencyKey);
        if (renewal is null)
            return Result.Failure(new Error("AddOn.RenewalNotFound", "Renewal was not scheduled."));

        return addOn.CompleteRenewal(
            renewal.Id,
            externalPaymentReference: "platform-support-renewal",
            actorUserId,
            nowUtc
        );
    }

    private static TenantAddOnRenewal? FindRenewalByKey(TenantAddOn addOn, string idempotencyKey)
    {
        foreach (var renewal in addOn.Renewals)
        {
            if (renewal.IdempotencyKey == idempotencyKey)
                return renewal;
        }

        return null;
    }
}
