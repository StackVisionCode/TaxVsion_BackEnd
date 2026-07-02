namespace TaxVision.Customer.Application.Customers.Commands.Activate;

public sealed record ActivateCustomerCommand(Guid TenantId, Guid CustomerId, Guid ModifiedByUserId);
