using BuildingBlocks.Platform;
using BuildingBlocks.TimeZones;
using TaxVision.Wallet.Application.Settings.Abstractions;
using TaxVision.Wallet.Domain.Settings;

namespace TaxVision.Wallet.Tests.TestDoubles;

/// <summary>IWalletSettingsRepository en memoria.</summary>
public sealed class FakeWalletSettingsRepository : IWalletSettingsRepository
{
    public List<WalletSettings> Settings { get; } = [];
    public List<WalletSettingsChange> Changes { get; } = [];

    public Task<WalletSettings?> GetAsync(Guid tenantId, CancellationToken ct = default) =>
        Task.FromResult(Settings.FirstOrDefault(s => s.TenantId == tenantId));

    public Task AddAsync(WalletSettings settings, CancellationToken ct = default)
    {
        Settings.Add(settings);
        return Task.CompletedTask;
    }

    public Task AddChangeAsync(WalletSettingsChange change, CancellationToken ct = default)
    {
        Changes.Add(change);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<WalletSettingsChange>> ListChangesAsync(
        Guid tenantId,
        int page,
        int size,
        CancellationToken ct = default
    ) =>
        Task.FromResult<IReadOnlyList<WalletSettingsChange>>(
            Changes
                .Where(c => c.TenantId == tenantId)
                .OrderByDescending(c => c.AtUtc)
                .Skip((page - 1) * size)
                .Take(size)
                .ToList()
        );
}

/// <summary>Zona fija de la oficina.</summary>
public sealed class FakeOfficeTimeZone(string zoneId = "UTC") : IOfficeTimeZone
{
    public Task<ResolvedTimeZone> ResolveAsync(Guid tenantId, CancellationToken ct = default) =>
        Task.FromResult(ResolvedTimeZone.Resolve(zoneId));
}

public sealed class FakePlatformSettings(string brandName = "TaxOffix") : IPlatformSettings
{
    public PlatformSettingsSnapshot Current { get; } =
        new(1, brandName, brandName, "example.test", "api.example.test", "s@example.test", "b@example.test");
}
