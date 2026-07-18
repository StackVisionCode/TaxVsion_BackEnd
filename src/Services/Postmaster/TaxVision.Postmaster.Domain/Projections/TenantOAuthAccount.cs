namespace TaxVision.Postmaster.Domain.Projections;

/// <summary>
/// Proyección local de solo lectura de las cuentas OAuth (Gmail/Graph) que un tenant conectó en
/// Connectors — mismo patrón que <c>CustomerEmailProjection</c> (Signature) y
/// <c>UserDirectoryEntry</c>/<c>CustomerDirectoryEntry</c> (Communication). Se alimenta de
/// <c>connectors.tenant_email_account.connected.v1</c>/<c>.disconnected.v1</c> (D3 §4.3) — evita una
/// llamada M2M a Connectors en cada intento de resolver el provider de envío para un tenant.
/// </summary>
public sealed class TenantOAuthAccount
{
    private TenantOAuthAccount() { }

    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid AccountId { get; private set; }
    public string ProviderCode { get; private set; } = default!;
    public string FromAddress { get; private set; } = default!;
    public bool IsActive { get; private set; }
    public DateTime ConnectedAtUtc { get; private set; }
    public DateTime? DisconnectedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    public static TenantOAuthAccount ForNewConnection(
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

        return new TenantOAuthAccount
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
