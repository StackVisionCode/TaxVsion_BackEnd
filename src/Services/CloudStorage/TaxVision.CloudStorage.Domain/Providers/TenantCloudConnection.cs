using BuildingBlocks.Domain;

namespace TaxVision.CloudStorage.Domain.Providers;

/// <summary>Conexión OAuth de almacenamiento asociada exclusivamente a un tenant.</summary>
public sealed class TenantCloudConnection : TenantEntity
{
    private TenantCloudConnection() { }

    public string Provider { get; private set; } = "google-drive";
    public string AccountEmail { get; private set; } = string.Empty;
    public string ProtectedRefreshToken { get; private set; } = string.Empty;
    public DateTime ConnectedAtUtc { get; private set; }

    public void UpdateProtectedRefreshToken(string protectedRefreshToken) =>
        ProtectedRefreshToken = protectedRefreshToken;

    public static TenantCloudConnection Connect(Guid tenantId, string email, string protectedRefreshToken) =>
        Connect(tenantId, "google-drive", email, protectedRefreshToken);

    public static TenantCloudConnection Connect(
        Guid tenantId,
        string provider,
        string email,
        string protectedRefreshToken
    )
    {
        var connection = new TenantCloudConnection
        {
            // A tenant can connect more than one cloud provider. The tenant/provider
            // pair is enforced by the unique index; each connection needs its own PK.
            Id = Guid.NewGuid(),
            Provider = provider,
            AccountEmail = email,
            ProtectedRefreshToken = protectedRefreshToken,
            ConnectedAtUtc = DateTime.UtcNow,
        };
        connection.SetTenant(tenantId);
        return connection;
    }
}

/// <summary>Nonce OAuth de un solo uso; tenant/usuario nunca se aceptan desde el callback.</summary>
public sealed class CloudProviderOAuthState : TenantEntity
{
    private CloudProviderOAuthState() { }

    public const int ReturnOriginMaxLength = 255;

    public Guid InitiatedByUserId { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }

    /// <summary>Proveedor que lo emitió: un state de Drive no sirve para completar Dropbox.</summary>
    public string Provider { get; private set; } = string.Empty;

    /// <summary>Origen del CRM que inició (subdominio del tenant). Se valida al volver, nunca se usa crudo.</summary>
    public string? ReturnOrigin { get; private set; }

    // El Id viaja como `state` en la URL del proveedor: aleatorio, nunca el TenantId (predecible y
    // único por tenant, que además bloqueaba un segundo intento mientras el primero no venciera).
    public static CloudProviderOAuthState Create(Guid tenantId, Guid userId, string provider, string? returnOrigin)
    {
        var state = new CloudProviderOAuthState
        {
            Id = Guid.NewGuid(),
            InitiatedByUserId = userId,
            ExpiresAtUtc = DateTime.UtcNow.AddMinutes(10),
            Provider = provider,
            ReturnOrigin = returnOrigin is { Length: > 0 and <= ReturnOriginMaxLength } ? returnOrigin : null,
        };
        state.SetTenant(tenantId);
        return state;
    }

    public bool CanComplete(string provider, DateTime nowUtc) => Provider == provider && ExpiresAtUtc > nowUtc;
}

public sealed class TenantCloudFileCopy : TenantEntity
{
    private TenantCloudFileCopy() { }

    public Guid FileId { get; private set; }
    public string Provider { get; private set; } = "google-drive";
    public string RemoteFileId { get; private set; } = string.Empty;
    public string ChecksumSha256 { get; private set; } = string.Empty;

    public static TenantCloudFileCopy Create(Guid tenantId, Guid fileId, string remoteFileId, string checksum) =>
        Create(tenantId, fileId, "google-drive", remoteFileId, checksum);

    public static TenantCloudFileCopy Create(
        Guid tenantId,
        Guid fileId,
        string provider,
        string remoteFileId,
        string checksum
    )
    {
        var copy = new TenantCloudFileCopy
        {
            FileId = fileId,
            Provider = provider,
            RemoteFileId = remoteFileId,
            ChecksumSha256 = checksum,
        };
        copy.SetTenant(tenantId);
        return copy;
    }
}
