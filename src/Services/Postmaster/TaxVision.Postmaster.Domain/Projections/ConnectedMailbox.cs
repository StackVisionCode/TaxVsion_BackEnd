using BuildingBlocks.Domain;

namespace TaxVision.Postmaster.Domain.Projections;

/// <summary>
/// Buzón que la oficina conectó en Connectors y por el que Postmaster puede enviar. Vale para los
/// tres proveedores: Gmail, Graph y SMTP manual.
///
/// <para>Proyección local de solo lectura, alimentada por
/// <c>connectors.tenant_email_account.connected.v1</c>/<c>.disconnected.v1</c> — evita una llamada
/// M2M a Connectors en cada envío.</para>
///
/// <para>Se llamaba <c>TenantOAuthAccount</c>, de cuando solo había OAuth. El nombre hizo creer que
/// el carril no servía para SMTP, y por esa confusión se construyó un escalón que sobraba.</para>
/// </summary>
public sealed class ConnectedMailbox
{
    private ConnectedMailbox() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid AccountId { get; private set; }
    public string ProviderCode { get; private set; } = default!;
    public string FromAddress { get; private set; } = default!;
    public bool IsActive { get; private set; }
    public DateTime ConnectedAtUtc { get; private set; }
    public DateTime? DisconnectedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    /// <summary>Dueño del buzón personal; null en el de oficina (solo cuenta si <see cref="OwnershipKnown"/>).</summary>
    [UserReference(
        UserReferenceKind.Projection,
        HandledBy = "Connectors:ConnectorsUserOffboardedConsumer",
        Via = "ConnectorsTenantEmailAccountDisconnectedIntegrationEvent",
        Note = "Copia del dueño en Connectors: el retiro desconecta allá y el evento desactiva esta fila."
    )]
    public Guid? OwnerUserId { get; private set; }

    /// <summary>False en filas y avisos anteriores a estos campos: hasta reconciliar con Connectors, no se usa.</summary>
    public bool OwnershipKnown { get; private set; }

    /// <summary>DM-02: los correos de la oficina solo salen por un buzón de oficina, nunca por uno personal.</summary>
    public bool IsOfficeMailbox => OwnershipKnown && OwnerUserId is null;

    public static ConnectedMailbox ForNewConnection(
        Guid tenantId,
        Guid accountId,
        string providerCode,
        string fromAddress,
        DateTime connectedAtUtc,
        Guid? ownerUserId = null,
        bool ownershipKnown = true
    )
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId is required.", nameof(tenantId));
        if (accountId == Guid.Empty)
            throw new ArgumentException("AccountId is required.", nameof(accountId));
        if (string.IsNullOrWhiteSpace(providerCode))
            throw new ArgumentException("ProviderCode is required.", nameof(providerCode));
        if (string.IsNullOrWhiteSpace(fromAddress))
            throw new ArgumentException("FromAddress is required.", nameof(fromAddress));

        return new ConnectedMailbox
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            AccountId = accountId,
            ProviderCode = providerCode,
            FromAddress = fromAddress,
            IsActive = true,
            ConnectedAtUtc = connectedAtUtc,
            DisconnectedAtUtc = null,
            UpdatedAtUtc = connectedAtUtc,
            OwnerUserId = ownerUserId,
            OwnershipKnown = ownershipKnown,
        };
    }

    /// <summary>
    /// Lápida: la desconexión llegó antes que cualquier alta. La fila queda inactiva con la fecha del retiro y frena
    /// a un alta más vieja que llegue después.
    /// </summary>
    public static ConnectedMailbox Tombstone(
        Guid tenantId,
        Guid accountId,
        string providerCode,
        string fromAddress,
        DateTime disconnectedAtUtc
    )
    {
        var mailbox = ForNewConnection(
            tenantId,
            accountId,
            providerCode,
            fromAddress,
            disconnectedAtUtc,
            ownershipKnown: false
        );
        mailbox.IsActive = false;
        mailbox.DisconnectedAtUtc = disconnectedAtUtc;
        return mailbox;
    }

    /// <summary>False si el aviso es anterior al último aplicado: la cola no garantiza el orden.</summary>
    public bool ReconnectAt(
        string providerCode,
        string fromAddress,
        DateTime connectedAtUtc,
        Guid? ownerUserId = null,
        bool ownershipKnown = false
    )
    {
        if (connectedAtUtc < UpdatedAtUtc)
            return false;

        // Un aviso sin dueño (emisor viejo) no borra lo que ya se sabe.
        if (ownershipKnown)
            ApplyOwnership(ownerUserId);

        ProviderCode = providerCode;
        FromAddress = fromAddress;
        IsActive = true;
        ConnectedAtUtc = connectedAtUtc;
        DisconnectedAtUtc = null;
        UpdatedAtUtc = connectedAtUtc;
        return true;
    }

    /// <summary>Dueño leído de Connectors (aviso nuevo o reconciliación). No mueve la marca de orden.</summary>
    public void ApplyOwnership(Guid? ownerUserId)
    {
        OwnerUserId = ownerUserId;
        OwnershipKnown = true;
    }

    /// <summary>False si el aviso es anterior al último aplicado: la cola no garantiza el orden.</summary>
    public bool MarkDisconnected(DateTime disconnectedAtUtc)
    {
        if (disconnectedAtUtc < UpdatedAtUtc)
            return false;

        IsActive = false;
        DisconnectedAtUtc = disconnectedAtUtc;
        UpdatedAtUtc = disconnectedAtUtc;
        return true;
    }
}
