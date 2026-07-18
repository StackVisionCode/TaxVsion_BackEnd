namespace BuildingBlocks.Messaging.ConnectorsIntegrationEvents;

/// <summary>
/// Un tenant desconectó una cuenta de correo — conceptualmente
/// <c>connectors.tenant_email_account.disconnected.v1</c> (§19 del plan de Connectors). Postmaster
/// lo consume para dar de baja su proyección local (D3 §4.3) — un envío vía TenantOAuth para esta
/// cuenta debe fallar limpio después de esto, no intentar contra un token ya revocado.
/// </summary>
public sealed record ConnectorsTenantEmailAccountDisconnectedIntegrationEvent : IntegrationEvent
{
    public required Guid AccountId { get; init; }
    public required string EmailAddress { get; init; }
    public required string ProviderCode { get; init; }
    public required DateTime DisconnectedAtUtc { get; init; }
}
