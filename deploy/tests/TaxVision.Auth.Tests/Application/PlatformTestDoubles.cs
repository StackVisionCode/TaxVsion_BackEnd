using BuildingBlocks.Platform;
using TaxVision.Auth.Application.Abstractions;

namespace TaxVision.Auth.Tests.Application;

/// <summary>Dominio de plataforma fijo para los tests (en producción sale de Tenant).</summary>
internal sealed class FixedPlatformSettings(PlatformSettingsSnapshot current) : IPlatformSettings
{
    public PlatformSettingsSnapshot Current { get; } = current;

    public static FixedPlatformSettings For(string baseDomain, string? apiHost = null) =>
        new(
            new PlatformSettingsSnapshot(
                1,
                "TaxOffix",
                "TaxOffix LLC",
                baseDomain,
                apiHost ?? $"api.{baseDomain}",
                $"support@{baseDomain}",
                $"billing@{baseDomain}"
            )
        );
}

/// <summary>Cache de resolución en memoria que además registra qué hosts se invalidaron.</summary>
internal sealed class RecordingResolutionCache : ITenantResolutionCache
{
    private readonly Dictionary<string, Guid> _entries = new(StringComparer.Ordinal);

    public List<string> Invalidated { get; } = [];

    public Task<Guid?> TryGetAsync(string host, CancellationToken ct = default) =>
        Task.FromResult(_entries.TryGetValue(host, out var tenantId) ? tenantId : (Guid?)null);

    public Task SetAsync(string host, Guid tenantId, CancellationToken ct = default)
    {
        _entries[host] = tenantId;
        return Task.CompletedTask;
    }

    public Task InvalidateAsync(string host, CancellationToken ct = default)
    {
        Invalidated.Add(host);
        _entries.Remove(host);
        return Task.CompletedTask;
    }
}
