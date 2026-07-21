namespace BuildingBlocks.Messaging.ConnectorsIntegrationEvents;

/// <summary>
/// El refresh proactivo de un access token OAuth agotó reintentos/circuit breaker.
/// TenantEmailAccount.Status ya quedó en Error — este evento es la alerta operacional
/// (failure #1 documentado de integraciones Gmail: ver Connectors_Service_Design_And_Implementation_Plan.md §29).
/// </summary>
public sealed record ConnectorsOAuthRefreshFailedIntegrationEvent : IntegrationEvent
{
    public required Guid AccountId { get; init; }
    public required Guid ConnectionId { get; init; }
    public required string ProviderCode { get; init; }
    public required string Reason { get; init; }
    public required string ErrorCode { get; init; }
    public required DateTime FailedAtUtc { get; init; }
    public required Guid CreatedByUserId { get; init; }
}
