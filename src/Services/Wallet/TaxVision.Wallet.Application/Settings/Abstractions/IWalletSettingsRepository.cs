using BuildingBlocks.TimeZones;
using TaxVision.Wallet.Domain.Settings;

namespace TaxVision.Wallet.Application.Settings.Abstractions;

/// <summary>Ajustes del monedero (uno por oficina) y su historia append-only.</summary>
public interface IWalletSettingsRepository
{
    /// <summary>Los ajustes del tenant (rastreados), o null si nunca se guardaron (valen los de fábrica).</summary>
    Task<WalletSettings?> GetAsync(Guid tenantId, CancellationToken ct = default);

    Task AddAsync(WalletSettings settings, CancellationToken ct = default);

    Task AddChangeAsync(WalletSettingsChange change, CancellationToken ct = default);

    /// <summary>Historia del tenant, más reciente primero.</summary>
    Task<IReadOnlyList<WalletSettingsChange>> ListChangesAsync(
        Guid tenantId,
        int page,
        int size,
        CancellationToken ct = default
    );
}

/// <summary>Zona horaria de la oficina (proyección local de Tenant). Sin proyección devuelve UTC marcado.</summary>
public interface IOfficeTimeZone
{
    Task<ResolvedTimeZone> ResolveAsync(Guid tenantId, CancellationToken ct = default);
}
