using System.Globalization;
using TaxVision.Documents.Application.Abstractions;

namespace TaxVision.Documents.Application.Generations.Receipts;

/// <summary>
/// Lo que comparten los dos recibos (el del onboarding y el de una compra de un tenant): el bloque del
/// emisor y la elección del logo. El resto —a quién se le cobró y por qué— lo pone cada uno, porque un
/// onboarding le factura a una persona que todavía no tiene oficina y una compra le factura a la oficina.
/// </summary>
public static class ReceiptRenderData
{
    public static Dictionary<string, object> Issuer(IssuerSnapshot issuer, string? brandLogoDataUri) =>
        new()
        {
            ["name"] = issuer.Name,
            ["taxId"] = issuer.TaxId,
            ["addressLine1"] = issuer.AddressLine1,
            ["city"] = issuer.City,
            ["state"] = issuer.State,
            ["postalCode"] = issuer.PostalCode,
            ["country"] = issuer.Country,
            ["phone"] = issuer.Phone,
            ["email"] = issuer.Email,
            ["website"] = issuer.Website,
            ["logo"] = ResolveLogo(brandLogoDataUri, issuer.LogoDataUri),
        };

    public static string Money(long amountCents) => (amountCents / 100m).ToString("N2", CultureInfo.InvariantCulture);

    public static string PaidAt(DateTime paidAtUtc) =>
        paidAtUtc.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture);

    /// <summary>Prefiere el logo de marca; si no hay, el de config. Nunca falla: la plantilla lo omite
    /// cuando queda vacío.</summary>
    public static string ResolveLogo(string? brandLogoDataUri, string? configLogoDataUri)
    {
        foreach (var candidate in new[] { brandLogoDataUri, configLogoDataUri })
        {
            if (candidate is { Length: > 0 } logo && logo.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                return logo;
        }

        return string.Empty;
    }
}
