namespace BuildingBlocks.Messaging.ConnectorsIntegrationEvents;

/// <summary>
/// Un tenant conectó una cuenta de correo (Gmail/Graph vía OAuth, o IMAP) — conceptualmente
/// <c>connectors.tenant_email_account.connected.v1</c> (§19 del plan de Connectors). Postmaster
/// consume esto para mantener su proyección local de qué cuentas OAuth activas existen por tenant
/// (D3 §4.3 — evita una llamada M2M a Connectors en cada intento de resolver el provider de envío).
/// </summary>
public sealed record ConnectorsTenantEmailAccountConnectedIntegrationEvent : IntegrationEvent
{
    public required Guid AccountId { get; init; }
    public required string EmailAddress { get; init; }
    public required string ProviderCode { get; init; }
    public required DateTime ConnectedAtUtc { get; init; }
}
