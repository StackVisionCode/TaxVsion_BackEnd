namespace TaxVision.Notification.Application.Common;

/// <summary>
/// Motivo de un cobro fallido en palabras de cliente, en el idioma de quien lee. El código del proveedor (Stripe,
/// PayPal o propio) nunca llega al correo tal cual: es técnico y a veces trae datos internos.
/// </summary>
public static class ChargeFailureReasons
{
    public const string Generic = "The payment could not be completed.";

    public static string Describe(string? failureCode, string? language = null) =>
        EmailText.For($"charge_failure.{KeyOf(failureCode)}", language);

    private static string KeyOf(string? failureCode)
    {
        var code = (failureCode ?? string.Empty).Trim().ToLowerInvariant();
        if (code.Length == 0)
            return "generic";

        if (code.Contains("requires_payment_method") || code.Contains("missingtoken"))
            return "no_payment_method";
        if (code.Contains("insufficient_funds"))
            return "insufficient_funds";
        if (code.Contains("expired"))
            return "card_expired";
        if (code.Contains("cvc"))
            return "cvc";
        if (code.Contains("lost_card") || code.Contains("stolen_card") || code.Contains("pickup_card"))
            return "card_unusable";
        if (code.Contains("authentication") || code.Contains("payeraction") || code.Contains("requires_action"))
            return "authentication";
        if (code.Contains("processing_error") || code.Contains("unavailable") || code.Contains("timeout"))
            return "processor";
        if (code.Contains("declin") || code.Contains("do_not_honor"))
            return "declined";

        return "generic";
    }
}
