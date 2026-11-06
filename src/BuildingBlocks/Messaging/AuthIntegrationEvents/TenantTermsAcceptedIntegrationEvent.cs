using Wolverine.Attributes;

namespace BuildingBlocks.Messaging.AuthIntegrationEvents;

/// <summary>Fase L1.4 — publicado cuando un tenant acepta la version vigente del ToS/AUP.</summary>
[MessageIdentity("BuildingBlocks.Messaging.AuthIntegrationEvents.TenantTermsAcceptedIntegrationEvent")]
public sealed record TenantTermsAcceptedIntegrationEvent : IntegrationEvent
{
    public required Guid AcceptedByUserId { get; init; }
    public required string TermsVersion { get; init; }
}
