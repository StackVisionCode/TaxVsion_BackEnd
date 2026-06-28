namespace TaxVision.Customer.Application.Customers.Queries.Search;

public sealed record SearchCustomersQuery(string? Term = null, int Page = 1, int Size = 20);
