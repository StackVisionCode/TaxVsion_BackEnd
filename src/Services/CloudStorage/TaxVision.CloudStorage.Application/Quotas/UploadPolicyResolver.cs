using System.Collections.Concurrent;
using Microsoft.Extensions.Options;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Configuration;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.Quotas;

namespace TaxVision.CloudStorage.Application.Quotas;

/// <summary>
/// Caché por réplica del tamaño resuelto, por (folderType, planCode, tenantId). Singleton: los handlers
/// que escriben filas llaman <see cref="Invalidate"/> y la siguiente resolución vuelve a leer la BD.
/// </summary>
public sealed class UploadPolicyCache
{
    public static readonly TimeSpan TimeToLive = TimeSpan.FromSeconds(60);

    private readonly ConcurrentDictionary<UploadPolicyCacheKey, (long Bytes, DateTime ExpiresAtUtc)> _entries = new();

    public bool TryGet(UploadPolicyCacheKey key, DateTime nowUtc, out long maxFileSizeBytes)
    {
        if (_entries.TryGetValue(key, out var entry) && entry.ExpiresAtUtc > nowUtc)
        {
            maxFileSizeBytes = entry.Bytes;
            return true;
        }
        maxFileSizeBytes = 0;
        return false;
    }

    public void Set(UploadPolicyCacheKey key, long maxFileSizeBytes, DateTime nowUtc) =>
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
    IOptions<CloudStorageOptions> options,
    UploadPolicyCache cache,
    ISystemClock clock
) : IUploadPolicyResolver
{
    public async Task<EffectiveUploadPolicy> ResolveAsync(
        Guid tenantId,
        string planCode,
        FolderType folderType,
        CancellationToken ct
    )
    {
        // Los tipos permitidos siguen viniendo del código; solo el tamaño se administra en BD (D-33).
        var fallback = options.Value.ResolveUploadPolicy(planCode, folderType);
        var key = UploadPolicyCacheKey.For(folderType, planCode, tenantId);

        if (!cache.TryGet(key, clock.UtcNow, out var maxFileSizeBytes))
        {
            var rows = await policies.ListApplicableAsync(folderType, planCode, tenantId, ct);
            maxFileSizeBytes = PickMostSpecific(rows) ?? fallback.MaxFileSizeBytes;
            cache.Set(key, maxFileSizeBytes, clock.UtcNow);
        }

        return fallback with
        {
            MaxFileSizeBytes = maxFileSizeBytes,
        };
    }

    /// <summary>Tenant > Plan > Global. El override de BD puede superar el MaxSizeBytes de la folder policy; el único techo es el técnico.</summary>
    private static long? PickMostSpecific(IReadOnlyList<UploadSizePolicy> rows)
    {
        if (rows.Count == 0)
            return null;
        var winner = rows.OrderByDescending(r => (int)r.Scope).First();
        return Math.Clamp(winner.MaxFileSizeBytes, 1, UploadSizePolicy.TechnicalMaxBytes);
    }
}
