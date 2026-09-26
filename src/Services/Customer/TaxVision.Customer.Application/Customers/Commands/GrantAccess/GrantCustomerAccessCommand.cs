namespace TaxVision.Customer.Application.Customers.Commands.GrantAccess;

public sealed record GrantCustomerAccessCommand(Guid TenantId, Guid CustomerId, Guid UserId, Guid GrantedByUserId);
