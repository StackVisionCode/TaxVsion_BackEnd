namespace TaxVision.Billing.Domain.ValueObjects;

/// <summary>Plazo de pago de la factura. Se configura por tenant y cada factura lo congela al crearse.</summary>
public enum PaymentTerms
{
    DueOnReceipt,
    Net7,
    Net15,
    Net30,
    Net45,
}

public static class PaymentTermsExtensions
{
    /// <summary>Días entre la emisión y el vencimiento.</summary>
    public static int NetDays(this PaymentTerms terms) =>
        terms switch
        {
            PaymentTerms.Net7 => 7,
            PaymentTerms.Net15 => 15,
            PaymentTerms.Net30 => 30,
            PaymentTerms.Net45 => 45,
            _ => 0,
        };

    /// <summary>Texto que se imprime en el PDF.</summary>
    public static string Label(this PaymentTerms terms) =>
        terms switch
        {
            PaymentTerms.Net7 => "Net 7",
            PaymentTerms.Net15 => "Net 15",
            PaymentTerms.Net30 => "Net 30",
            PaymentTerms.Net45 => "Net 45",
            _ => "Due on receipt",
        };

    /// <summary>Solo el nombre exacto (sin distinguir mayúsculas); rechaza números ("3") y valores fuera del enum.</summary>
    public static bool TryParse(string? raw, out PaymentTerms terms)
    {
        terms = PaymentTerms.DueOnReceipt;
        var trimmed = raw?.Trim();
        if (string.IsNullOrEmpty(trimmed) || !char.IsLetter(trimmed[0]))
            return false;
        return Enum.TryParse(trimmed, ignoreCase: true, out terms) && Enum.IsDefined(terms);
    }
}

/// <summary>Condiciones de cobro congeladas en una factura: plazo, instrucciones de pago y pie.</summary>
public sealed record InvoiceTermsSnapshot(PaymentTerms PaymentTerms, string? PaymentInstructions, string? InvoiceFooter)
{
    /// <summary>Sin configuración guardada: vence al recibirla, sin instrucciones ni pie.</summary>
    public static InvoiceTermsSnapshot Default { get; } = new(PaymentTerms.DueOnReceipt, null, null);
}
