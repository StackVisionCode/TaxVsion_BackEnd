using System.Globalization;

namespace TaxVision.Auth.Application.Subscriptions.IntegrationEvents;

/// <summary>Importe para el copy de los correos de cobro: "$49.00" en dólares, "49.00 EUR" en otra moneda.</summary>
public static class ChargeAmountFormat
{
    public static string Format(long amountCents, string? currency)
    {
        var amount = (amountCents / 100m).ToString("N2", CultureInfo.InvariantCulture);
        var code = string.IsNullOrWhiteSpace(currency) ? "USD" : currency.Trim().ToUpperInvariant();
        return code == "USD" ? $"${amount}" : $"{amount} {code}";
    }
}
