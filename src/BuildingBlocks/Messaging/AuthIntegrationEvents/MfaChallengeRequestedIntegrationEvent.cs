namespace BuildingBlocks.Messaging.AuthIntegrationEvents;

/// <summary>
/// Publicado por Auth cuando se genera un OTP por email o SMS
/// (login MFA, verificación de email/teléfono). Notification entrega el código.
/// </summary>
public sealed record MfaChallengeRequestedIntegrationEvent : IntegrationEvent
{
    public required Guid UserId { get; init; }

    /// <summary>"Email" o "Sms".</summary>
    public required string Channel { get; init; }

    /// <summary>Email o número de teléfono destino.</summary>
    public required string Destination { get; init; }

    /// <summary>Código OTP en claro para entrega. No se persiste en claro en ningún servicio.</summary>
    public required string Code { get; init; }

    /// <summary>"login", "email_change" o "phone_verification".</summary>
    public required string Purpose { get; init; }

    public required DateTime ExpiresAtUtc { get; init; }
}
