namespace TaxVision.Customer.Application.Customers.Queries.GetById;

public sealed record GetCustomerByIdQuery(Guid TenantId, Guid CustomerId);
