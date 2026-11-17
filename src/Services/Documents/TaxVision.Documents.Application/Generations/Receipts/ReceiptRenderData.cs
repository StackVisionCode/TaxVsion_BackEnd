using System.Globalization;
using BuildingBlocks.TimeZones;
using BuildingBlocks.TimeZones.Formatting;
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

    /// <summary>Subtotal bruto, una fila por descuento y si hay alguno. Sin descuentos, el subtotal es lo pagado.</summary>
    public static Dictionary<string, object> Totals(long paidCents, IReadOnlyList<ReceiptDiscount> discounts)
    {
        var rows = discounts.Where(discount => discount.AmountCents > 0).ToList();
        return new()
        {
            ["subtotal"] = Money(paidCents + rows.Sum(discount => discount.AmountCents)),
            ["hasDiscounts"] = rows.Count > 0,
            ["discounts"] = rows.Select(discount => new Dictionary<string, object>
                {
                    ["label"] = discount.Label,
                    ["amount"] = Money(discount.AmountCents),
                })
                .ToList(),
        };
    }

    /// <summary>P7: el día del pago en la zona de quien paga, sin hora (como los recibos de la industria).</summary>
    public static string PaidAt(DateTime paidAtUtc, string? timeZoneId) =>
        ZonedTimeFormatter.Date(paidAtUtc, ResolvedTimeZone.Resolve(timeZoneId));

    /// <summary>Día del cobro para quien paga: nombre del archivo y carpeta del año.</summary>
    public static DateOnly PaidDay(DateTime paidAtUtc, string? timeZoneId) =>
        CivilDate.Of(paidAtUtc, ResolvedTimeZone.Resolve(timeZoneId));

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

/// <summary>Una fila de descuento del recibo: cómo se muestra (código enmascarado) y cuánto restó.</summary>
public sealed record ReceiptDiscount(string Label, long AmountCents);
