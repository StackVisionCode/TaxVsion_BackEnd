namespace BuildingBlocks.Messaging.AuthIntegrationEvents;

/// <summary>
/// Publicado por Auth ante eventos de seguridad relevantes: reutilización de refresh
/// token, lockout por fuerza bruta, login desde dispositivo nuevo, MFA desactivado.
/// </summary>
public sealed record SecurityAlertIntegrationEvent : IntegrationEvent
{
    public required Guid UserId { get; init; }
    public required string AlertType { get; init; }
    public string? IpAddress { get; init; }
    public string? UserAgent { get; init; }
    public string? DetailsJson { get; init; }
}

public static class SecurityAlertType
{
    public const string TokenReuseDetected = "token_reuse_detected";
    public const string AccountLockedOut = "account_locked_out";
    public const string MfaDisabled = "mfa_disabled";
    public const string PasswordChanged = "password_changed";
    public const string EmailChanged = "email_changed";
}
