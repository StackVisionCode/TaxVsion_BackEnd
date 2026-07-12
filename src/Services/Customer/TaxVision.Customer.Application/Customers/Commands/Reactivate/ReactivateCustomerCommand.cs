namespace TaxVision.Customer.Application.Customers.Commands.Reactivate;

public sealed record ReactivateCustomerCommand(Guid TenantId, Guid CustomerId, Guid ModifiedByUserId);
