namespace TaxVision.Customer.Application.Customers.Queries.CheckExists;

public sealed record CheckCustomerExistsQuery(Guid TenantId, string? Email, string? TaxIdentifier);
