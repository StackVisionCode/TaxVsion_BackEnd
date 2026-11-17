using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Domain.AddOns;

namespace TaxVision.Subscription.Application.AddOns.TierChange;

/// <summary>¿Caben los archivos de la oficina si cambia un paquete de storage por otro? Lo decide el servidor con
/// el uso real (CloudStorage) y la cuota vigente (foto de entitlements); sin alguno de los dos no se adivina.</summary>
public static class AddOnTierChangeStorage
{
    private const string StorageEntitlementKey = "storage.max_bytes";

    public static readonly Error UsageUnavailable = new(
        "AddOn.StorageUsageUnavailable",
        "We couldn't check your storage usage. Please try again in a moment."
    );

    public static readonly Error WouldOverflow = new(
        "AddOn.StorageWouldOverflow",
        "Your files don't fit in the smaller size. Free up space first."
    );

    /// <summary>null = no se pudo medir.</summary>
    public static async Task<bool?> WouldFitAsync(
        AddOnTierChangeContext context,
        IStorageUsageClient usage,
        ITenantEntitlementSnapshotRepository snapshots,
        CancellationToken ct
    )
    {
        var snapshot = await snapshots.GetByTenantIdAsync(context.AddOn.TenantId, ct);
        if (!long.TryParse(snapshot?.FindByKey(StorageEntitlementKey)?.Value, out var maxBytes))
            return null;

        var used = await usage.GetUsedBytesAsync(context.AddOn.TenantId, ct);
        return used is null ? null : AddOnTierChangePolicy.Fits(used.Value, maxBytes, context.Current, context.Target);
    }

    public static async Task<Result> EnsureFitsAsync(
        AddOnTierChangeContext context,
        IStorageUsageClient usage,
        ITenantEntitlementSnapshotRepository snapshots,
        CancellationToken ct
    ) =>
        await WouldFitAsync(context, usage, snapshots, ct) switch
        {
            true => Result.Success(),
            false => Result.Failure(WouldOverflow),
            null => Result.Failure(UsageUnavailable),
        };

    /// <summary>Cuota vigente de la foto de entitlements; null si no hay foto.</summary>
    public static async Task<long?> CurrentMaxBytesAsync(
        Guid tenantId,
        ITenantEntitlementSnapshotRepository snapshots,
        CancellationToken ct
    )
    {
        var snapshot = await snapshots.GetByTenantIdAsync(tenantId, ct);
        return long.TryParse(snapshot?.FindByKey(StorageEntitlementKey)?.Value, out var bytes) ? bytes : null;
    }
}
