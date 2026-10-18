using BuildingBlocks.Domain;

namespace TaxVision.Auth.Domain.Users.Events;

public sealed record UserSlotPriorityAssigned(Guid TenantId, Guid UserId, DateTime SlotPriorityAt) : IDomainEvent;
