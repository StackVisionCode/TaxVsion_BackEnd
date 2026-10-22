namespace TaxVision.Subscription.Application.Subscriptions.IntegrationEvents;

/// <summary>Cómo se nombra en el correo (en inglés) lo que se intentó renovar.</summary>
public static class RenewalItemDescriptions
{
    public const string Seat = "1 additional user seat";

    /// <summary>"Professional plan". Si el nombre ya termina en "plan" no se repite; sin nombre, genérico.</summary>
    public static string Plan(string? planName)
    {
        if (string.IsNullOrWhiteSpace(planName))
            return "your subscription";

        var name = planName.Trim();
        return name.EndsWith(" plan", StringComparison.OrdinalIgnoreCase) ? name : $"{name} plan";
    }

    /// <summary>"Email add-on". Sin nombre, genérico.</summary>
    public static string AddOn(string? addOnName)
    {
        if (string.IsNullOrWhiteSpace(addOnName))
            return "your add-on";

        var name = addOnName.Trim();
        return name.EndsWith(" add-on", StringComparison.OrdinalIgnoreCase) ? name : $"{name} add-on";
    }
}
