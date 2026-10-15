namespace BuildingBlocks.Messaging.AuthIntegrationEvents;

/// <summary>OBF-2.7 — publicado por el sweeper OnboardingOrphanDetectorJob cuando un onboarding tiene
/// pago cobrado pero sigue sin completar el office-setup form después de la ventana configurada. Es la
/// señal para que un consumer en Notification (o cualquier otro servicio de alerting) notifique al
/// PlatformAdmin — hay dinero del cliente involucrado y un flujo de recuperación admin disponible
/// (OnboardingAdminController.Reactivate/ResendRegistrationEmail). Se publica una vez por aggregate por
/// tick: la dedup la hace el consumer, idealmente con un cache per OnboardingId para no spamear si el
/// huérfano persiste varios ticks.</summary>
public sealed record OnboardingOrphanDetectedIntegrationEvent : IntegrationEvent
{
    public required Guid OnboardingId { get; init; }
    public required string Email { get; init; }
    public required string FirstName { get; init; }
    public required string LastName { get; init; }
    public required DateTime PaymentCompletedAtUtc { get; init; }
    public required DateTime CreatedAtUtc { get; init; }
    public required long GrossAmountCents { get; init; }
    public required string Currency { get; init; }
    public required string Status { get; init; }
    public DateTime DetectedAtUtc { get; init; } = DateTime.UtcNow;
}
