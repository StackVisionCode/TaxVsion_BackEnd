using BuildingBlocks.TimeZones;
using TaxVision.Wallet.Application.Settings.Abstractions;
using TaxVision.Wallet.Domain.Settings;

namespace TaxVision.Wallet.Application.Settings;

/// <summary>Respuesta de <c>GET /wallet/settings</c> (y de los PUT/DELETE que la cambian).</summary>
public sealed record WalletSettingsView(
    long LowBalanceThresholdMicros,
    AutoRechargeView AutoRecharge,
    ConsentTemplateView ConsentTemplate
);

public sealed record AutoRechargeView(
    string Status,
    string? PausedReason,
    long AmountCents,
    long ThresholdMicros,
    int DailyLimit,
    string? Provider,
    string? MethodReference,
    string? MethodLabel,
    ConsentView? Consent,
    int RechargesToday
);

public sealed record ConsentView(Guid AcceptedByUserId, DateTime AcceptedAtUtc, string TextVersion, string Text);

/// <summary>Texto vigente con sus marcadores; el servidor arma el texto final al aceptar.</summary>
public sealed record ConsentTemplateView(string Version, string Text);

/// <summary>Item de <c>GET /wallet/settings/history</c>.</summary>
public sealed record WalletSettingsChangeView(DateTime At, Guid? UserId, string Action, string Detail)
{
    public static WalletSettingsChangeView From(WalletSettingsChange c) =>
        new(c.AtUtc, c.UserId, c.Action.ToString(), c.Detail);
}

public static class WalletSettingsViews
{
    public static ConsentTemplateView Template { get; } =
        new(AutoRechargeConsentTemplate.Version, AutoRechargeConsentTemplate.Text);

    /// <summary>Vista con el contador del día de la oficina.</summary>
    public static async Task<WalletSettingsView> BuildAsync(
        WalletSettings settings,
        IOfficeTimeZone officeTimeZone,
        CancellationToken ct
    )
    {
        var zone = await officeTimeZone.ResolveAsync(settings.TenantId, ct);
        return Build(settings, CivilDate.Of(DateTime.UtcNow, zone));
    }

    public static WalletSettingsView Build(WalletSettings s, DateOnly officeToday) =>
        new(
            s.LowBalanceThresholdMicros,
            new AutoRechargeView(
                s.AutoRechargeStatus.ToString(),
                s.AutoRechargePausedReason?.ToString(),
                s.AutoRechargeAmountCents,
                // Sin umbral propio todavía: se sugiere el de saldo bajo.
                s.AutoRechargeThresholdMicros > 0
                    ? s.AutoRechargeThresholdMicros
                    : s.LowBalanceThresholdMicros,
                WalletSettings.DailyRechargeLimit,
                s.AutoRechargeProvider,
                s.AutoRechargeMethodReference,
                s.AutoRechargeMethodLabel,
                Consent(s),
                s.RechargesOn(officeToday)
            ),
            Template
        );

    private static ConsentView? Consent(WalletSettings s) =>
        s
            is {
                ConsentAcceptedByUserId: { } user,
                ConsentAcceptedAtUtc: { } at,
                ConsentTextVersion: { } v,
                ConsentText: { } t
            }
            ? new ConsentView(user, at, v, t)
            : null;
}
