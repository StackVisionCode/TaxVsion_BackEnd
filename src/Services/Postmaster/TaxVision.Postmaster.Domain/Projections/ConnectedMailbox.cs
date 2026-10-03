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

    public static ConnectedMailbox ForNewConnection(
        Guid tenantId,
        Guid accountId,
        string providerCode,
        string fromAddress,
        DateTime connectedAtUtc
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
        };
    }

    public void ReconnectAt(string providerCode, string fromAddress, DateTime connectedAtUtc)
    {
        ProviderCode = providerCode;
        FromAddress = fromAddress;
        IsActive = true;
        ConnectedAtUtc = connectedAtUtc;
        DisconnectedAtUtc = null;
        UpdatedAtUtc = connectedAtUtc;
    }

    public void MarkDisconnected(DateTime disconnectedAtUtc)
    {
        IsActive = false;
        DisconnectedAtUtc = disconnectedAtUtc;
        UpdatedAtUtc = disconnectedAtUtc;
    }
}
