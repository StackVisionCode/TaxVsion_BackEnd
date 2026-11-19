using System.Globalization;

namespace TaxVision.Wallet.Domain.Settings;

/// <summary>
/// Texto versionado que el administrador acepta antes de encender la recarga automática. Cambiar el texto exige
/// subir <see cref="Version"/>: una aceptación de una versión vieja se rechaza.
/// </summary>
public static class AutoRechargeConsentTemplate
{
    public const string Version = "2026-11-18";

    public const string Text =
        "I authorize {platform} to charge {amount} to {method} each time the wallet balance drops below {threshold}, "
        + "up to {dailyLimit} times a day ({dailyMax}). Each charge is emailed as a receipt. I can turn this off at any time.";

    public static string Render(string platform, long amountCents, string methodLabel, long thresholdMicros) =>
        Text.Replace("{platform}", platform, StringComparison.Ordinal)
            .Replace("{amount}", WalletAmounts.FromCents(amountCents), StringComparison.Ordinal)
            .Replace("{method}", methodLabel, StringComparison.Ordinal)
            .Replace("{threshold}", WalletAmounts.FromMicros(thresholdMicros), StringComparison.Ordinal)
            .Replace(
                "{dailyLimit}",
                WalletSettings.DailyRechargeLimit.ToString(CultureInfo.InvariantCulture),
                StringComparison.Ordinal
            )
            .Replace(
                "{dailyMax}",
                WalletAmounts.FromCents(amountCents * WalletSettings.DailyRechargeLimit),
                StringComparison.Ordinal
            );
}

/// <summary>Montos en dólares para textos ("$25.00"), siempre con cultura invariante.</summary>
public static class WalletAmounts
{
    public static string FromCents(long cents) => Format(cents / 100m);

    public static string FromMicros(long micros) => Format(micros / 1_000_000m);

    private static string Format(decimal dollars) => "$" + dollars.ToString("#,0.00", CultureInfo.InvariantCulture);
}
