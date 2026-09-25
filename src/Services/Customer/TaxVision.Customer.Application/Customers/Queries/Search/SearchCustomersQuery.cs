namespace TaxVision.Customer.Application.Customers.Queries.Search;

// ActorUserId + CanViewAll para la visibilidad por asignación (el handler decide si restringe según el flag).
public sealed record SearchCustomersQuery(
    Guid TenantId,
    string? Term,
    CustomerStatusFilter Status,
    int Page,
    int Size,
    Guid ActorUserId,
    bool CanViewAll
);
