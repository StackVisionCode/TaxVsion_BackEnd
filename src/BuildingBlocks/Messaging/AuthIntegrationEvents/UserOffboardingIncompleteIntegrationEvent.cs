namespace BuildingBlocks.Messaging.AuthIntegrationEvents;

/// <summary>
/// Publicado por Auth cuando el retiro de un usuario no recibió todos sus acuses en el plazo configurado.
/// Notification avisa a quienes gestionan usuarios con las partes pendientes en palabras de la oficina.
/// </summary>
public sealed record UserOffboardingIncompleteIntegrationEvent : IntegrationEvent
{
    public required Guid UserId { get; init; }

    /// <summary>Nombre visible del retirado (Auth lo conoce; Notification ya no lo tiene activo).</summary>
    public required string UserDisplayName { get; init; }

    public required DateTime StartedAtUtc { get; init; }
    public required int ExpectedSteps { get; init; }
    public required int CompletedSteps { get; init; }

    /// <summary>Pasos sin acuse.</summary>
    public IReadOnlyList<PendingOffboardingStep> PendingSteps { get; init; } = [];
}

/// <summary>Un paso del retiro sin acuse. <see cref="Module"/> = nombre que ve la oficina (p. ej. "Calendar").</summary>
public sealed record PendingOffboardingStep(string Service, string Step, string Module);
