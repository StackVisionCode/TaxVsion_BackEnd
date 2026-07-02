namespace TaxVision.Customer.Application.Customers.Commands.RequestPortalInvitation;

public sealed record RequestPortalInvitationCommand(Guid TenantId, Guid CustomerId, Guid RequestedByUserId);

public sealed record RequestPortalInvitationResponse(
    Guid CustomerId,
    string Email,
    string Status // "Requested"
);
