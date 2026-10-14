using TaxVision.CloudStorage.Domain.CustomerAccess;

namespace TaxVision.CloudStorage.Application.CustomerAccess.Commands.SetDenyOverride;

/// <summary>
/// Declara una regla deny sobre un (customer, item, capability). Idempotente: si ya existe, actualiza
/// autor/fecha/reason sin crear fila duplicada. Audit siempre registra una fila Created por mutación.
/// </summary>
/// <param name="HasManageAny">Permiso admin global (<c>cloudstorage.customer_access.manage_any</c>). Si true, bypassa la asignación.</param>
/// <param name="HasManageAssigned">Permiso delegado (<c>cloudstorage.customer_access.manage_assigned</c>). Requiere que el customer esté asignado al actor.</param>
public sealed record SetDenyOverrideCommand(
    Guid TenantId,
    Guid ActorUserId,
    Guid CustomerId,
    AccessResourceType ResourceType,
    Guid ResourceId,
    AccessCapability Capability,
    string? Reason,
    bool HasManageAny,
    bool HasManageAssigned
);

public sealed record SetDenyOverrideResponse(CustomerAccessRuleView Rule, bool Created);
