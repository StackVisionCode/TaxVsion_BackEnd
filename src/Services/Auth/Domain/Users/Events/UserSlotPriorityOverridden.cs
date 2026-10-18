using BuildingBlocks.Domain;

namespace TaxVision.Auth.Domain.Users.Events;

public sealed record UserSlotPriorityOverridden(
    Guid TenantId,
    Guid UserId,
    DateTime? PreviousAt,
    DateTime? NewAt,
    Guid ActorAdminId,
    string Reason
) : IDomainEvent;
