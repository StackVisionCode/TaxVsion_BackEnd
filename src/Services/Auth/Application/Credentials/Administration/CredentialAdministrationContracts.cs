using System.Text.Json.Serialization;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.Credentials;
using TaxVision.Auth.Domain.Users;

namespace TaxVision.Auth.Application.Credentials.Administration;

// Contratos de la administración de credenciales por la oficina (03 §15). Nunca llevan token, link ni correo.

public enum ResetLinkStatus
{
    None,
    Pending,
    Completed,
    Expired,
    Cancelled,
    Superseded,
}

public enum ResetLinkDelivery
{
    Unknown,
    Queued,
    Sent,
    Delivered,
    Failed,
    Bounced,
}

public enum ResetLinkIssueOutcome
{
    Issued,
    AlreadyPending,
}

/// <summary>Último link de reset del usuario. <see cref="Delivery"/> se llena al leer el panel (Unknown por defecto).</summary>
public sealed record ResetLinkSummary(
    Guid ResetTokenId,
    ResetOrigin Origin,
    ResetLinkStatus Status,
    DateTime CreatedAtUtc,
    DateTime ExpiresAtUtc,
    ResetLinkDelivery Delivery
)
{
    public static ResetLinkSummary? From(PasswordResetToken? token, DateTime utcNow) =>
        token is null
            ? null
            : new(
                token.Id,
                token.Origin,
                ToLinkStatus(token.StatusAt(utcNow)),
                token.CreatedAtUtc,
                token.ExpiresAtUtc,
                ResetLinkDelivery.Unknown
            );

    // Agotado se muestra como vencido; revocado por cambio de contraseña o por asegurar, como cancelado.
    public static ResetLinkStatus ToLinkStatus(PasswordResetTokenStatus status) =>
        status switch
        {
            PasswordResetTokenStatus.Pending => ResetLinkStatus.Pending,
            PasswordResetTokenStatus.Completed => ResetLinkStatus.Completed,
            PasswordResetTokenStatus.Expired or PasswordResetTokenStatus.Exhausted => ResetLinkStatus.Expired,
            PasswordResetTokenStatus.Superseded => ResetLinkStatus.Superseded,
            _ => ResetLinkStatus.Cancelled,
        };
}

/// <summary>
/// Traduce el estado del <c>NotificationLog</c> a la pill del panel (04 §4.3). Sent, Delivered, Bounced y Failed solo
/// con lo que confirma Notification; sin fila todavía, un link reciente sigue "enviándose".
/// </summary>
public static class ResetLinkDeliveryResolver
{
    // Notification procesa el evento en segundos; pasado esto, sin fila no se puede afirmar nada.
    private static readonly TimeSpan QueuedWindow = TimeSpan.FromMinutes(10);

    public static ResetLinkDelivery Resolve(NotificationEmailLookup lookup, DateTime createdAtUtc, DateTime utcNow) =>
        lookup switch
        {
            { Reachable: false } => ResetLinkDelivery.Unknown,
            { Status: null } => utcNow - createdAtUtc < QueuedWindow
                ? ResetLinkDelivery.Queued
                : ResetLinkDelivery.Unknown,
            { Status: "Pending" } => ResetLinkDelivery.Queued,
            { Status: "Sent" } => ResetLinkDelivery.Sent,
            { Status: "Delivered" } => ResetLinkDelivery.Delivered,
            { Status: "Bounced" } => ResetLinkDelivery.Bounced,
            { Status: "Failed" } => ResetLinkDelivery.Failed,
            _ => ResetLinkDelivery.Unknown,
        };
}

/// <summary>Botones que la política y el estado permiten pintar. <see cref="ResetMfa"/> solo con MFA activo.</summary>
public sealed record StaffCredentialActions(bool SendResetLink, bool RequireChange, bool Secure, bool ResetMfa);

/// <summary>
/// Motivo de <i>Require change</i> y <i>Secure account</i> (03 §15). La nota (≤ 256) queda solo en el usuario.
/// <see cref="IdentityConfirmed"/> (opcional, IA-C08) solo se audita.
/// </summary>
public sealed record CredentialAdministrationRequest(
    [property: JsonConverter(typeof(ReasonCodeJsonConverter))] CredentialAdministrationReasonCode ReasonCode,
    string? ReasonNote,
    bool? IdentityConfirmed = null
);

public sealed record CredentialStateChangedResponse(
    CredentialStatus CredentialStatus,
    bool AlreadyInState,
    int SessionsRevoked
);

/// <summary><see cref="AlreadyInState"/>: la cuenta ya estaba asegurada (reenvío del link seguro, AJ-15).</summary>
public sealed record AccountSecuredResponse(
    int SessionsRevoked,
    int TrustedDevicesRevoked,
    int LinksRevoked,
    Guid ResetTokenId,
    bool AlreadyInState = false
);

/// <summary>
/// <see cref="AdminLinkLifetimeHours"/>: vida del link que se emitiría ahora (para el diálogo).
/// <see cref="EmailChangeCooldownUntilUtc"/>: pausa de links tras una corrección de correo de la oficina (SR-04).
/// </summary>
public sealed record StaffCredentialStatusResponse(
    Guid UserId,
    CredentialStatus CredentialStatus,
    CredentialAdministrationReasonCode? ReasonCode,
    DateTime? CredentialStateChangedAtUtc,
    DateTime? PasswordChangedAtUtc,
    bool MfaEnabled,
    bool EmailVerified,
    ResetLinkSummary? LatestResetLink,
    StaffCredentialActions AllowedActions,
    int AdminLinkLifetimeHours,
    DateTime? EmailChangeCooldownUntilUtc = null
);

/// <summary>
/// Mismo panel para la cuenta del portal de un cliente. Sin cuenta: <see cref="HasPortalAccount"/> false.
/// <see cref="EmailChangeCooldownUntilUtc"/>: hasta cuándo siguen pausados los links tras un cambio de correo de la
/// oficina (D-08); null sin pausa.
/// </summary>
public sealed record PortalCredentialStatusResponse(
    Guid CustomerId,
    bool HasPortalAccount,
    CredentialStatus CredentialStatus,
    CredentialAdministrationReasonCode? ReasonCode,
    DateTime? CredentialStateChangedAtUtc,
    DateTime? PasswordChangedAtUtc,
    bool MfaEnabled,
    bool EmailVerified,
    ResetLinkSummary? LatestResetLink,
    StaffCredentialActions AllowedActions,
    int AdminLinkLifetimeHours,
    DateTime? EmailChangeCooldownUntilUtc = null
);

public sealed record ResetLinkIssuedResponse(ResetLinkIssueOutcome Outcome, Guid ResetTokenId, DateTime ExpiresAtUtc);

/// <summary>Reset de MFA por la oficina: <c>Reset</c> o <c>NotEnabled</c> (no había nada que quitar).</summary>
public sealed record StaffMfaResetResponse(string Outcome, int SessionsRevoked);
