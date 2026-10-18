using BuildingBlocks.Domain;

namespace TaxVision.Auth.Domain.Users.Events;

public sealed record UserSlotPriorityReleased(Guid TenantId, Guid UserId) : IDomainEvent;
