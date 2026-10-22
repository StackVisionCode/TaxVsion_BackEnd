namespace TaxVision.Notification.Application.Common;

/// <summary>Qué cambió para el cliente tras el último intento fallido, dicho en el correo.</summary>
public static class ChargeFailureConsequences
{
    public static string Describe(string? consequence, string? itemDescription) =>
        consequence switch
        {
            "SubscriptionSuspended" => "your subscription has been suspended and your team can no longer sign in",
            "SeatSuspended" => "the user seat has been suspended and the person assigned to it can no longer sign in",
            "AddOnSuspended" => $"{AddOnSubject(itemDescription)} has been paused",
            _ => "the service will remain on hold until the payment is completed",
        };

    private static string AddOnSubject(string? itemDescription) =>
        string.IsNullOrWhiteSpace(itemDescription) ? "the add-on" : $"the {itemDescription}";
}
