namespace TaxVision.Notification.Application.Consumers.ClientForms;

/// <summary>Un email no es un nombre: sin nombre real, la plantilla saluda de forma genérica.</summary>
public static class ParticipantNames
{
    public static string? ForGreeting(string? name) =>
        string.IsNullOrWhiteSpace(name) || name.Contains('@') ? null : name.Trim();
}
