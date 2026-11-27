using BuildingBlocks.Results;

namespace TaxVision.Customer.Application.Abstractions;

// Espejo de los contratos de Auth (03 §15). Los enums viajan como texto y se devuelven tal cual al CRM.

public sealed record ResetLinkSummary(
    Guid ResetTokenId,
    string Origin,
    string Status,
    DateTime CreatedAtUtc,
    DateTime ExpiresAtUtc,
    string Delivery
);

public sealed record PortalCredentialActions(bool SendResetLink, bool RequireChange, bool Secure, bool ResetMfa);

/// <summary>
/// Credencial del portal del cliente para la ficha. <c>HasPortalAccount</c> false = aún sin acceso.
/// <c>EmailChangeCooldownUntilUtc</c>: links pausados hasta esa fecha tras un cambio de correo de la oficina (D-08).
/// </summary>
public sealed record PortalCredentialStatusResponse(
    Guid CustomerId,
    bool HasPortalAccount,
    string CredentialStatus,
    string? ReasonCode,
    DateTime? CredentialStateChangedAtUtc,
    DateTime? PasswordChangedAtUtc,
    bool MfaEnabled,
    bool EmailVerified,
    ResetLinkSummary? LatestResetLink,
    PortalCredentialActions AllowedActions,
    int AdminLinkLifetimeHours = 24,
    DateTime? EmailChangeCooldownUntilUtc = null
);

/// <summary><c>Outcome</c>: <c>Issued</c> o <c>AlreadyPending</c>. Sin token, link ni correo.</summary>
public sealed record ResetLinkIssuedResponse(string Outcome, Guid ResetTokenId, DateTime ExpiresAtUtc);

/// <summary>
/// Quién pide, cuándo se reautenticó y desde dónde: Auth vuelve a validar la ventana y lo audita. El motivo (código
/// de Auth como texto y nota ≤ 256) solo en <i>require change</i> y <i>secure</i>; la nota nunca va a la auditoría.
/// </summary>
public sealed record PortalCredentialRequest(
    Guid RequestedByUserId,
    DateTime ReauthenticatedAtUtc,
    string? ActorIpAddress,
    string? ActorUserAgent,
    string? ReasonCode = null,
    string? ReasonNote = null,
    bool? IdentityConfirmed = null
);

/// <summary>Cuerpo del CRM para <i>require change</i> y <i>secure</i> (03 §15). Auth valida el código.</summary>
public sealed record PortalCredentialAdministrationRequest(
    string ReasonCode,
    string? ReasonNote,
    bool? IdentityConfirmed = null
);

/// <summary>Estado tras <i>require change</i>. <c>AlreadyInState</c>: ya estaba pedido (200 idempotente).</summary>
public sealed record CredentialStateChangedResponse(string CredentialStatus, bool AlreadyInState, int SessionsRevoked);

/// <summary>Resultado de <i>secure</i>. <c>AlreadyInState</c>: ya estaba asegurada y se reenvió el link seguro.</summary>
public sealed record AccountSecuredResponse(
    int SessionsRevoked,
    int TrustedDevicesRevoked,
    int LinksRevoked,
    Guid ResetTokenId,
    bool AlreadyInState = false
);

/// <summary>Endpoints M2M de Auth para la credencial del portal de un cliente (link, cambio obligatorio y asegurar).</summary>
public interface ICustomerPortalCredentialsClient
{
    Task<Result<PortalCredentialStatusResponse>> GetStatusAsync(
        Guid tenantId,
        Guid customerId,
        Guid requestedByUserId,
        CancellationToken ct = default
    );

    Task<Result<ResetLinkIssuedResponse>> SendResetLinkAsync(
        Guid tenantId,
        Guid customerId,
        PortalCredentialRequest request,
        CancellationToken ct = default
    );

    Task<Result> CancelResetLinkAsync(
        Guid tenantId,
        Guid customerId,
        PortalCredentialRequest request,
        CancellationToken ct = default
    );

    Task<Result<CredentialStateChangedResponse>> RequireChangeAsync(
        Guid tenantId,
        Guid customerId,
        PortalCredentialRequest request,
        CancellationToken ct = default
    );

    Task<Result<AccountSecuredResponse>> SecureAsync(
        Guid tenantId,
        Guid customerId,
        PortalCredentialRequest request,
        CancellationToken ct = default
    );
}
