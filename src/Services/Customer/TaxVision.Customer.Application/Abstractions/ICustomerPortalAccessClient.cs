using BuildingBlocks.Results;

namespace TaxVision.Customer.Application.Abstractions;

/// <summary>Desenlace del pedido de acceso al portal: <c>Invited</c>, <c>Resent</c> o <c>AlreadyActive</c>.</summary>
public sealed record PortalInvitationOutcome(string Outcome, string Email, DateTime? ExpiresAtUtc);

/// <summary>Segundo factor del portal del cliente. <c>HasPortalAccount</c> false = aún sin acceso al portal.</summary>
public sealed record PortalMfaStatus(bool HasPortalAccount, bool MfaEnabled, bool RequiredByPolicy);

/// <summary>Desenlace del reset del segundo factor: <c>Reset</c> o <c>NotEnabled</c> (no tenía nada que quitar).</summary>
public sealed record PortalMfaResetOutcome(string Outcome);

/// <summary>
/// Auth es quien crea las cuentas de login: Customer le pide el acceso al portal de un cliente (con el email
/// que Customer tiene registrado) y recibe el desenlace o el motivo del rechazo en la misma llamada.
/// </summary>
public interface ICustomerPortalAccessClient
{
    Task<Result<PortalInvitationOutcome>> IssueInvitationAsync(
        Guid tenantId,
        Guid customerId,
        string email,
        Guid requestedByUserId,
        CancellationToken ct = default
    );

    /// <summary>¿Tiene acceso al portal, segundo factor activo y la oficina lo exige?</summary>
    Task<Result<PortalMfaStatus>> GetMfaStatusAsync(Guid tenantId, Guid customerId, CancellationToken ct = default);

    /// <summary>Quita el segundo factor del portal del cliente (perdió el teléfono); queda auditado en Auth.</summary>
    Task<Result<PortalMfaResetOutcome>> ResetMfaAsync(
        Guid tenantId,
        Guid customerId,
        Guid requestedByUserId,
        CancellationToken ct = default
    );
}
