using TaxVision.Customer.Domain.Customers;

namespace TaxVision.Customer.Application.Customers.Queries.Search;

// ActorUserId + CanViewAll para la visibilidad por asignación (el handler decide si restringe según el flag).
// Kind null = individuos y empresas.
public sealed record SearchCustomersQuery(
    Guid TenantId,
    string? Term,
    CustomerStatusFilter Status,
    int Page,
    int Size,
    Guid ActorUserId,
    bool CanViewAll,
    CustomerKind? Kind = null,
    CustomerSortField Sort = CustomerSortField.Name,
    CustomerSortDirection Direction = CustomerSortDirection.Asc
);
