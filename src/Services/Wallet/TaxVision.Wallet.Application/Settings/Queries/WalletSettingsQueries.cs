using BuildingBlocks.TimeZones;
using TaxVision.Wallet.Application.Settings.Abstractions;
using TaxVision.Wallet.Domain.Settings;

namespace TaxVision.Wallet.Application.Settings.Queries;

/// <summary>Ajustes vigentes; sin fila devuelve los de fábrica sin crearla.</summary>
public sealed record GetWalletSettingsQuery(Guid TenantId);

/// <summary>Historia de cambios, más reciente primero.</summary>
public sealed record ListWalletSettingsHistoryQuery(Guid TenantId, int Page, int Size);

public static class GetWalletSettingsHandler
{
    public static async Task<WalletSettingsView> Handle(
        GetWalletSettingsQuery query,
        IWalletSettingsRepository repository,
        IOfficeTimeZone officeTimeZone,
        CancellationToken ct
    )
    {
        var settings = await repository.GetAsync(query.TenantId, ct) ?? WalletSettings.CreateDefault(query.TenantId);
        var zone = await officeTimeZone.ResolveAsync(query.TenantId, ct);
        return WalletSettingsViews.Build(settings, CivilDate.Of(DateTime.UtcNow, zone));
    }
}

public static class ListWalletSettingsHistoryHandler
{
    public static async Task<IReadOnlyList<WalletSettingsChangeView>> Handle(
        ListWalletSettingsHistoryQuery query,
        IWalletSettingsRepository repository,
        CancellationToken ct
    )
    {
        var changes = await repository.ListChangesAsync(query.TenantId, query.Page, query.Size, ct);
        return changes.Select(WalletSettingsChangeView.From).ToList();
    }
}
