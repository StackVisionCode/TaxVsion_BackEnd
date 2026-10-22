using System.Globalization;

namespace TaxVision.Notification.Application.Common;

/// <summary>Marca de la tarjeta como la reconoce el cliente: Stripe la guarda en minúsculas ("visa", "amex").</summary>
public static class CardBrandNames
{
    private static readonly Dictionary<string, string> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        ["visa"] = "Visa",
        ["mastercard"] = "Mastercard",
        ["amex"] = "American Express",
        ["american_express"] = "American Express",
        ["discover"] = "Discover",
        ["diners"] = "Diners Club",
        ["jcb"] = "JCB",
        ["unionpay"] = "UnionPay",
    };

    public static string Display(string? brand)
    {
        if (string.IsNullOrWhiteSpace(brand))
            return "card";

        var trimmed = brand.Trim();
        return Known.TryGetValue(trimmed, out var name)
            ? name
            : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(trimmed.ToLowerInvariant());
    }
}
