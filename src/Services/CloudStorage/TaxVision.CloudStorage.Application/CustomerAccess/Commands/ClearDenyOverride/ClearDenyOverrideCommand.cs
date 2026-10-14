using TaxVision.CloudStorage.Domain.CustomerAccess;

namespace TaxVision.CloudStorage.Application.CustomerAccess.Commands.ClearDenyOverride;

/// <summary>Elimina una regla deny. Idempotente: si no existe, success sin audit nuevo.</summary>
public sealed record ClearDenyOverrideCommand(
    Guid TenantId,
    Guid ActorUserId,
    Guid CustomerId,
    AccessResourceType ResourceType,
    Guid ResourceId,
    AccessCapability Capability,
    bool HasManageAny,
    bool HasManageAssigned
);

public sealed record ClearDenyOverrideResponse(bool Removed);
