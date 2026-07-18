using Wolverine.Attributes;

namespace BuildingBlocks.Messaging.EmailIntegrationEvents;

/// <summary>Opcional, solo para métricas (Connectors Fase 8 §36) — nunca lleva el body en sí, solo confirma que se sirvió.</summary>
[MessageIdentity("connectors.message_body_fetched.v1")]
public sealed record ConnectorsMessageBodyFetchedIntegrationEvent : IntegrationEvent
{
    public required Guid AccountId { get; init; }
    public required string ProviderMessageId { get; init; }
    public required long MimeSizeBytes { get; init; }
    public required DateTime FetchedAtUtc { get; init; }
}
