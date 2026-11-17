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

    /// <summary>El cobro del aniversario: "Pro plan, 3 seats and 1 add-on".</summary>
    public static string Bundle(string? planName, int seats, int addOns)
    {
        var parts = new List<string> { Plan(planName) };
        if (seats > 0)
            parts.Add(seats == 1 ? "1 seat" : $"{seats} seats");
        if (addOns > 0)
            parts.Add(addOns == 1 ? "1 add-on" : $"{addOns} add-ons");

        return parts.Count == 1 ? parts[0] : $"{string.Join(", ", parts[..^1])} and {parts[^1]}";
    }
}
