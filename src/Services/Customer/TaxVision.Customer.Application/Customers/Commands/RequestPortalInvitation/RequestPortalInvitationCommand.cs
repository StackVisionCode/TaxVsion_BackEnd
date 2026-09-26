namespace TaxVision.Customer.Application.Customers.Commands.RequestPortalInvitation;

public sealed record RequestPortalInvitationCommand(Guid TenantId, Guid CustomerId, Guid RequestedByUserId);

/// <param name="Status"><c>Invited</c> (correo enviado), <c>Resent</c> (había una invitación pendiente y se
/// reenvió) o <c>AlreadyActive</c> (el cliente ya tiene su portal; no se envía nada).</param>
public sealed record RequestPortalInvitationResponse(
    Guid CustomerId,
    string Email,
    string Status,
    DateTime? ExpiresAtUtc
);
