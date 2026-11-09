using Wolverine.Attributes;

namespace BuildingBlocks.Messaging.ConnectorsIntegrationEvents;

/// <summary>
/// Un tenant desconectó una cuenta de correo — conceptualmente
/// <c>connectors.tenant_email_account.disconnected.v1</c> (§19 del plan de Connectors). Postmaster
/// lo consume para dar de baja su proyección local (D3 §4.3) — un envío vía TenantMailbox para esta
/// cuenta debe fallar limpio después de esto, no intentar contra un token ya revocado.
/// </summary>
[MessageIdentity("BuildingBlocks.Messaging.ConnectorsIntegrationEvents.ConnectorsTenantEmailAccountDisconnectedIntegrationEvent")]
public sealed record ConnectorsTenantEmailAccountDisconnectedIntegrationEvent : IntegrationEvent
{
    public required Guid AccountId { get; init; }
    public required string EmailAddress { get; init; }
    public required string ProviderCode { get; init; }
    public required DateTime DisconnectedAtUtc { get; init; }

    /// <summary>Dueño del buzón personal; null en uno de oficina. Lo proyecta Correspondence (COR-10).</summary>
    public Guid? OwnerUserId { get; init; }

    /// <summary>Con <see cref="OwnerUserId"/> nulo y esto en false, el evento es anterior a estos campos.</summary>
    public bool IsOffice { get; init; }
}
