using BuildingBlocks.Results;

namespace TaxVision.Customer.Application.Abstractions;

/// <summary>Desenlace del pedido de acceso al portal: <c>Invited</c>, <c>Resent</c> o <c>AlreadyActive</c>.</summary>
public sealed record PortalInvitationOutcome(string Outcome, string Email, DateTime? ExpiresAtUtc);

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
}
