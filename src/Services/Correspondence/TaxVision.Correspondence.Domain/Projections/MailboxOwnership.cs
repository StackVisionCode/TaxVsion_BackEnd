using BuildingBlocks.Domain;

namespace TaxVision.Correspondence.Domain.Projections;

/// <summary>
/// De quién es cada buzón, proyectado de los eventos de Connectors (COR-10). Con esto la visibilidad de un empleado sin
/// permiso de oficina no llama a Connectors en cada lectura. Sin dueño = buzón de oficina.
/// </summary>
public sealed class MailboxOwnership : ITenantOwned
{
    private MailboxOwnership() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid AccountId { get; private set; }

    [UserReference(
        UserReferenceKind.Projection,
        HandledBy = "Connectors:ConnectorsUserOffboardedConsumer",
        Via = "ConnectorsTenantEmailAccountDisconnectedIntegrationEvent",
        Note = "Copia del dueño en Connectors: el retiro desconecta allá y el evento llega a esta proyección."
    )]
    public Guid? OwnerUserId { get; private set; }
    public bool IsActive { get; private set; }

    // Hora de Connectors del último cambio aplicado; lo más viejo que llegue tarde se ignora.
    public DateTime LastChangeAtUtc { get; private set; }

    public bool IsOffice => OwnerUserId is null;

    public void SetTenant(Guid tenantId) => TenantId = tenantId;

    public static MailboxOwnership Create(
        Guid tenantId,
        Guid accountId,
        Guid? ownerUserId,
        bool isActive,
        DateTime changedAtUtc
    ) =>
        new()
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            AccountId = accountId,
            OwnerUserId = ownerUserId,
            IsActive = isActive,
            LastChangeAtUtc = changedAtUtc,
        };

    /// <summary>Devuelve <c>false</c> si el cambio es más viejo que el último aplicado.</summary>
    public bool Apply(Guid? ownerUserId, bool isActive, DateTime changedAtUtc)
    {
        if (changedAtUtc < LastChangeAtUtc)
            return false;

        OwnerUserId = ownerUserId;
        IsActive = isActive;
        LastChangeAtUtc = changedAtUtc;
        return true;
    }
}
