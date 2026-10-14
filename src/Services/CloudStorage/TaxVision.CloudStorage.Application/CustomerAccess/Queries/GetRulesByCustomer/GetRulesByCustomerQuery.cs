namespace TaxVision.CloudStorage.Application.CustomerAccess.Queries.GetRulesByCustomer;

/// <summary>Lista las reglas deny declaradas sobre un cliente — payload del admin UI (y, en F6, del tooltip del portal).</summary>
public sealed record GetRulesByCustomerQuery(
    Guid TenantId,
    Guid ActorUserId,
    Guid CustomerId,
    bool HasManageAny,
    bool HasManageAssigned
);
