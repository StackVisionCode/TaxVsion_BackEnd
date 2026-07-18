namespace BuildingBlocks.Messaging.SignatureIntegrationEvents;

/// <summary>
/// Emitido por firmante al enviar la solicitud (Ready → InProgress). Contiene el enlace
/// público firmado con HMAC. Notification lo consume para dispatchar el correo/SMS con
/// la invitación al firmante.
///
/// <para>
/// La <c>PublicUrl</c> se considera PII de bajo riesgo (no expone tokens de sesión, sólo
/// da acceso a la solicitud puntual y expira). Aun así, no debe loggearse por encima de
/// <c>Debug</c>.
/// </para>
/// </summary>
public sealed record SignerInvitedIntegrationEvent : IntegrationEvent
{
    public required Guid SignatureRequestId { get; init; }
    public required Guid SignerId { get; init; }
    public required string Email { get; init; }
    public required string FullName { get; init; }
    public required int Order { get; init; }
    public required string Language { get; init; } // Es | En
    public required string PublicUrl { get; init; }
    public required DateTime ExpiresAtUtc { get; init; }
    public required int RevocationEpoch { get; init; }
    public required bool RequiresConsent { get; init; }
    public required bool RequiresSequentialSigning { get; init; }
}
