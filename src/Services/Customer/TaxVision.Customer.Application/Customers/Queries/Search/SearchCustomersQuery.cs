namespace TaxVision.Customer.Application.Customers.Queries.Search;

public sealed record SearchCustomersQuery(Guid TenantId, string? Term, CustomerStatusFilter Status, int Page, int Size);
