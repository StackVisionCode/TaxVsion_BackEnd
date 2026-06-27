using BuildingBlocks.Messaging;

namespace TaxVision.Auth.Application.Users.IntegrationEvents;

public sealed record UserRegisteredIntegrationEvent : IntegrationEvent
{
    public required Guid UserId { get; init; }
    public required string Email { get; init; }
    public required string ActorType { get; init; }
    public Guid? CustomerId { get; init; }
}
