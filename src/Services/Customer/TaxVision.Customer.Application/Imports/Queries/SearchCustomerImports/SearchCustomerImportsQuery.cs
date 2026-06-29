namespace TaxVision.Customer.Application.Imports.Queries.SearchCustomerImports;

public sealed record SearchCustomerImportsQuery(Guid TenantId, int Page, int Size);
