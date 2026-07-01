namespace TaxVision.Customer.Application.Customers.Commands.Deactivate;

public sealed record DeactivateCustomerCommand(Guid TenantId, Guid CustomerId, Guid ModifiedByUserId);
