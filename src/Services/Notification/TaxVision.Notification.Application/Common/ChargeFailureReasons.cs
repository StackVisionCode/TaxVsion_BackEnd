namespace TaxVision.Notification.Application.Common;

/// <summary>
/// Motivo de un cobro fallido en palabras de cliente. El código del proveedor (Stripe, PayPal o propio)
/// nunca llega al correo tal cual: es técnico y a veces trae datos internos.
/// </summary>
public static class ChargeFailureReasons
{
    public const string Generic = "The payment could not be completed.";

    public static string Describe(string? failureCode)
    {
        var code = (failureCode ?? string.Empty).Trim().ToLowerInvariant();
        if (code.Length == 0)
            return Generic;

        if (code.Contains("requires_payment_method") || code.Contains("missingtoken"))
            return "There is no payment method on file for automatic payments.";
        if (code.Contains("insufficient_funds"))
            return "The card doesn't have enough available funds.";
        if (code.Contains("expired"))
            return "The card on file has expired.";
        if (code.Contains("cvc"))
            return "The card's security code was not accepted.";
        if (code.Contains("lost_card") || code.Contains("stolen_card") || code.Contains("pickup_card"))
            return "The card on file can no longer be used.";
        if (code.Contains("authentication") || code.Contains("payeraction") || code.Contains("requires_action"))
            return "Your bank asked for an extra confirmation that can't be completed automatically.";
        if (code.Contains("processing_error") || code.Contains("unavailable") || code.Contains("timeout"))
            return "The payment processor had a temporary problem.";
        if (code.Contains("declin") || code.Contains("do_not_honor"))
            return "Your bank declined the charge.";

        return Generic;
    }
}
