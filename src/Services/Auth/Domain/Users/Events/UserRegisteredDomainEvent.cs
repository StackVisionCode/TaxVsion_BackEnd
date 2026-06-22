using BuildingBlocks.Domain;

namespace TaxVision.Auth.Domain.Users.Events;

public sealed record UserRegisteredDomainEvent(Guid UserId, Guid TenantId) : IDomainEvent
{
    public DateTime OccurredOn { get; } = DateTime.UtcNow;
}
