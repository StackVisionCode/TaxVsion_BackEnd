using BuildingBlocks.Common;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Common;

namespace TaxVision.Subscription.Application.AddOns.TierChange;

/// <summary>Deja sin efecto la bajada de tamaño programada; el paquete sigue igual.</summary>
public sealed record CancelAddOnTierChangeCommand(Guid TenantId, Guid TenantAddOnId, Guid RequestedByUserId);

public static class CancelAddOnTierChangeHandler
{
    public static async Task<Result> Handle(
        CancelAddOnTierChangeCommand command,
        ITenantAddOnRepository tenantAddOns,
        IUnitOfWork unitOfWork,
        ICorrelationContext correlation,
        ISubscriptionAuditLogWriter audit,
        CancellationToken ct
    )
    {
        var addOn = await tenantAddOns.GetByIdAsync(command.TenantAddOnId, command.TenantId, ct);
        if (addOn is null)
            return Result.Failure(new Error("AddOn.NotFound", "Add-on does not exist."));

        var pendingCode = addOn.PendingAddOnCode;
        var nowUtc = DateTime.UtcNow;
        var cancelled = addOn.CancelScheduledTierChange(command.RequestedByUserId, nowUtc);
        if (cancelled.IsFailure)
            return cancelled;

        await unitOfWork.SaveChangesAsync(ct);
        await AuditEntryFactory.AppendAsync(
            audit,
            command.TenantId,
            "TenantAddOn",
            addOn.Id,
            "AddOn.TierChangeCancelled",
            command.RequestedByUserId,
            correlation.CorrelationId,
            before: new { PendingAddOnCode = pendingCode },
            after: (object?)null,
            reason: null,
            nowUtc,
            ct
        );
        return Result.Success();
    }
}
