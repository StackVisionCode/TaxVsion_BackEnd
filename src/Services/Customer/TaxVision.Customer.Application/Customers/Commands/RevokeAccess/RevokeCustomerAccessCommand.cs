namespace TaxVision.Customer.Application.Customers.Commands.RevokeAccess;

public sealed record RevokeCustomerAccessCommand(Guid TenantId, Guid CustomerId, Guid UserId, Guid RevokedByUserId);
