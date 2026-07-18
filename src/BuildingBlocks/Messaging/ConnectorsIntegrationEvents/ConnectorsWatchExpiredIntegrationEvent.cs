namespace BuildingBlocks.Messaging.ConnectorsIntegrationEvents;

/// <summary>
/// El renewal de un ProviderWatchSubscription agotó reintentos (3 fallos consecutivos, Fase 6) —
/// TenantEmailAccount.Status ya quedó en Error. Alerta operacional crítica: sin watch activo la
/// cuenta deja de recibir push notifications y el sync se congela hasta un reauth manual
/// (POST /connectors/accounts/{id}/reauth).
/// </summary>
public sealed record ConnectorsWatchExpiredIntegrationEvent : IntegrationEvent
{
    public required Guid AccountId { get; init; }
    public required Guid SubscriptionId { get; init; }
    public required string ProviderCode { get; init; }
    public required int FailureCount { get; init; }
    public required DateTime ExpiredAtUtc { get; init; }
}
