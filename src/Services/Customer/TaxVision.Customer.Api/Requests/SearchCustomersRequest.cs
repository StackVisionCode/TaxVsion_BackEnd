using TaxVision.Customer.Application.Customers;
using TaxVision.Customer.Domain.Customers;

namespace TaxVision.Customer.Api.Requests;

// Cuerpo de POST /customers/search: el término (a menudo un email) viaja aquí y no en la URL,
// que queda en los logs del Gateway/proxy. Mismos filtros y defaults que GET /customers.
public sealed record SearchCustomersRequest(
    string? Term = null,
    CustomerStatusFilter Status = CustomerStatusFilter.Active,
    int Page = 1,
    int Size = 20,
    CustomerKind? Kind = null,
    CustomerSortField Sort = CustomerSortField.Name,
    CustomerSortDirection Dir = CustomerSortDirection.Asc
);
