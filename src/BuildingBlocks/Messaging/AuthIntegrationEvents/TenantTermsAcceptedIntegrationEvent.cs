namespace BuildingBlocks.Messaging.AuthIntegrationEvents;

/// <summary>Fase L1.4 — publicado cuando un tenant acepta la version vigente del ToS/AUP.</summary>
public sealed record TenantTermsAcceptedIntegrationEvent : IntegrationEvent
{
    public required Guid AcceptedByUserId { get; init; }
    public required string TermsVersion { get; init; }
}
