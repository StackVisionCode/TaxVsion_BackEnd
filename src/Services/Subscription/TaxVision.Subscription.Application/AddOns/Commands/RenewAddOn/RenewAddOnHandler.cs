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
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ILogger<TenantAddOn> logger,
        CancellationToken ct
    )
    {
        var addOn = await tenantAddOns.GetByIdAsync(command.TenantAddOnId, command.TenantId, ct);
        if (addOn is null)
            return Result.Failure(new Error("AddOn.NotFound", "Add-on does not exist."));

        var result = BeginAndCompleteRenewal(addOn, command.RequestedByUserId);
        if (result.IsFailure)
            return result;

        await unitOfWork.SaveChangesAsync(ct);
        await bus.InvokeAsync<Result>(new RecalculateEntitlementsCommand(command.TenantId), ct);

        logger.LogInformation("Add-on {TenantAddOnId} manually renewed (requested by {UserId}).", addOn.Id, command.RequestedByUserId);
        return Result.Success();
    }

    private static Result BeginAndCompleteRenewal(TenantAddOn addOn, Guid actorUserId)
    {
        var nowUtc = DateTime.UtcNow;
        var idempotencyKey = IdempotencyKeyFactory.AddOnRenewal(addOn.Id, addOn.CurrentPeriodEndUtc);

        var beginResult = addOn.BeginRenewal(idempotencyKey, actorUserId, nowUtc);
        if (beginResult.IsFailure)
            return beginResult;

        var renewal = FindRenewalByKey(addOn, idempotencyKey);
        if (renewal is null)
            return Result.Failure(new Error("AddOn.RenewalNotFound", "Renewal was not scheduled."));

        return addOn.CompleteRenewal(renewal.Id, externalPaymentReference: "manual-admin-renewal", actorUserId, nowUtc);
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
