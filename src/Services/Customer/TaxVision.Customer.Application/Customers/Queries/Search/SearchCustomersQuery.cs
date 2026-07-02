namespace TaxVision.Customer.Application.Customers.Queries.Search;

public sealed record SearchCustomersQuery(string? Term, CustomerStatusFilter Status, int Page, int Size);
