namespace TaxVision.Connectors.Application.Sync;

/// <summary>Tope contra un mensaje veneno: intentos (Connectors:Sync:MaxMessageFetchAttempts) y antigüedad mínima.</summary>
public sealed record MessageFetchFailureLimits(int MaxAttempts, TimeSpan MinAge)
{
    public const string MaxAttemptsKey = "Connectors:Sync:MaxMessageFetchAttempts";

    public static MessageFetchFailureLimits Default { get; } = new(10, TimeSpan.FromHours(24));
}
