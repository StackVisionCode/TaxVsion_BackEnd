using System.Collections.Concurrent;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.FileTypes;
using TaxVision.CloudStorage.Domain.Quotas;

namespace TaxVision.CloudStorage.Application.Quotas;

/// <summary>
/// Caché por réplica de la excepción de tamaño en BD, por (folderType, planCode, tenantId); null = no hay
/// fila. Singleton: los handlers que escriben filas llaman <see cref="Invalidate"/>.
/// </summary>
public sealed class UploadPolicyCache
{
    public static readonly TimeSpan TimeToLive = TimeSpan.FromSeconds(60);

    private readonly ConcurrentDictionary<UploadPolicyCacheKey, (long? Bytes, DateTime ExpiresAtUtc)> _entries = new();

    public bool TryGet(UploadPolicyCacheKey key, DateTime nowUtc, out long? maxFileSizeBytes)
    {
        if (_entries.TryGetValue(key, out var entry) && entry.ExpiresAtUtc > nowUtc)
        {
            maxFileSizeBytes = entry.Bytes;
            return true;
        }
        maxFileSizeBytes = null;
        return false;
    }

    public void Set(UploadPolicyCacheKey key, long? maxFileSizeBytes, DateTime nowUtc) =>
        _entries[key] = (maxFileSizeBytes, nowUtc + TimeToLive);

    public void Invalidate() => _entries.Clear();
}

public readonly record struct UploadPolicyCacheKey(FolderType FolderType, string PlanCode, Guid TenantId)
{
    public static UploadPolicyCacheKey For(FolderType folderType, string planCode, Guid tenantId) =>
        new(folderType, (planCode ?? string.Empty).Trim().ToLowerInvariant(), tenantId);
}

public sealed class UploadPolicyResolver(
    IUploadSizePolicyRepository policies,
    IFilePolicyCatalogRepository catalogRepository,
    FilePolicyCatalogCache catalogCache,
    UploadPolicyCache cache,
    ISystemClock clock
) : IUploadPolicyResolver
{
    public async Task<EffectiveUploadPolicy> ResolveAsync(
        Guid tenantId,
        TenantStorageLimit limit,
        FolderType folderType,
        bool forCustomerPortal,
        CancellationToken ct
    )
    {
        var catalog = await CatalogAsync(ct);
        var folder =
            catalog.FolderPolicies.GetValueOrDefault(folderType)
            ?? catalog.FolderPolicies.GetValueOrDefault(FolderType.Other)
            ?? throw new InvalidOperationException($"No folder file policy for {folderType} nor Other.");

        return EffectiveUploadPolicyCalculator.Calculate(
            folder,
            catalog.FileTypes,
            limit.PlanAllowance(),
            await SizeOverrideAsync(tenantId, limit.PlanCode, folderType, ct),
            forCustomerPortal ? limit.PortalRestriction() : null
        );
    }

    private async Task<FilePolicyCatalog> CatalogAsync(CancellationToken ct)
    {
        if (catalogCache.TryGet(clock.UtcNow, out var cached))
            return cached;

        var catalog = new FilePolicyCatalog(
            await catalogRepository.ListFileTypesAsync(ct),
            (await catalogRepository.ListFolderPoliciesAsync(ct)).ToDictionary(policy => policy.FolderType)
        );
        catalogCache.Set(catalog, clock.UtcNow);
        return catalog;
    }

    // Excepción de tamaño en BD (D-33): la fila más específica gana. Null = sin excepción (decide carpeta/plan).
    private async Task<long?> SizeOverrideAsync(Guid tenantId, string planCode, FolderType folderType, CancellationToken ct)
    {
        var key = UploadPolicyCacheKey.For(folderType, planCode, tenantId);
        if (cache.TryGet(key, clock.UtcNow, out var cached))
            return cached;

        var rows = await policies.ListApplicableAsync(folderType, planCode, tenantId, ct);
        var value = PickMostSpecific(rows);
        cache.Set(key, value, clock.UtcNow);
        return value;
    }

    /// <summary>Tenant > Plan > Global. El override de BD puede superar el techo de la carpeta; el único techo es el técnico.</summary>
    private static long? PickMostSpecific(IReadOnlyList<UploadSizePolicy> rows)
    {
        if (rows.Count == 0)
            return null;
        var winner = rows.OrderByDescending(r => (int)r.Scope).First();
        return Math.Clamp(winner.MaxFileSizeBytes, 1, UploadSizePolicy.TechnicalMaxBytes);
    }
}
