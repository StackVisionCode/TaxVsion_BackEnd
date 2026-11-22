namespace TaxVision.Notification.Application.Common;

/// <summary>Qué cambió para el cliente tras el último intento fallido, dicho en el correo y en el idioma de quien lee.</summary>
public static class ChargeFailureConsequences
{
    public static string Describe(string? consequence, string? itemDescription, string? language = null) =>
        consequence switch
        {
            "SubscriptionSuspended" => EmailText.For("charge_failure_effect.subscription_suspended", language),
            "SeatSuspended" => EmailText.For("charge_failure_effect.seat_suspended", language),
            "AddOnSuspended" => EmailText.For(
                "charge_failure_effect.add_on_suspended",
                language,
                new Dictionary<string, string> { ["addOn"] = AddOnSubject(itemDescription, language) }
            ),
            _ => EmailText.For("charge_failure_effect.on_hold", language),
        };

    private static string AddOnSubject(string? itemDescription, string? language) =>
        string.IsNullOrWhiteSpace(itemDescription)
            ? EmailText.For("charge_failure_effect.add_on_generic", language)
            : EmailText.For(
                "charge_failure_effect.add_on",
                language,
                new Dictionary<string, string> { ["item"] = itemDescription }
            );
}
