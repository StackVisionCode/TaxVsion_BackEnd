using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Domain.AddOns;
using Wolverine.Attributes;

namespace TaxVision.Subscription.Application.AddOns.TierChange;

/// <summary>Qué pasaría al cambiar de tamaño, sin cambiar nada.</summary>
public sealed record GetAddOnTierChangePreviewQuery(Guid TenantId, Guid TenantAddOnId, string TargetCode);

/// <param name="WouldFit">Solo en una bajada: si los archivos caben hoy; null si no se pudo medir.</param>
public sealed record AddOnTierChangePreviewResponse(
    string Direction,
    long ChargeNowCents,
    long TargetUnitPriceCents,
    string Currency,
    DateTime EffectiveAtUtc,
    bool? WouldFit
);

public static class GetAddOnTierChangePreviewHandler
{
    // Mide el uso en CloudStorage: sin transacción abierta (BB-8).
    [NonTransactional]
    public static async Task<Result<AddOnTierChangePreviewResponse>> Handle(
        GetAddOnTierChangePreviewQuery query,
        AddOnTierChangeLoader loader,
        IStorageUsageClient usage,
        ITenantEntitlementSnapshotRepository snapshots,
        CancellationToken ct
    )
    {
        var loaded = await loader.LoadAsync(query.TenantId, query.TenantAddOnId, query.TargetCode, DateTime.UtcNow, ct);
        if (loaded.IsFailure)
            return Result.Failure<AddOnTierChangePreviewResponse>(loaded.Error);

        var context = loaded.Value;
        var plan = context.Plan;
        bool? wouldFit =
            plan.Direction == AddOnTierChangeDirection.Downgrade
                ? await AddOnTierChangeStorage.WouldFitAsync(context, usage, snapshots, ct)
                : null;

        return Result.Success(
            new AddOnTierChangePreviewResponse(
                plan.Direction.ToString(),
                plan.ChargeNow.Cents,
                plan.TargetUnitPrice.Cents,
                plan.TargetUnitPrice.Currency,
                plan.EffectiveAtUtc,
                wouldFit
            )
        );
    }
}
