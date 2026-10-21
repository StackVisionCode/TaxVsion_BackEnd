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

    public void UpdateProtectedRefreshToken(string protectedRefreshToken) => ProtectedRefreshToken = protectedRefreshToken;

    public static TenantCloudConnection Connect(Guid tenantId, string email, string protectedRefreshToken)
        => Connect(tenantId, "google-drive", email, protectedRefreshToken);

    public static TenantCloudConnection Connect(Guid tenantId, string provider, string email, string protectedRefreshToken)
    {
        var connection = new TenantCloudConnection
        {
            // A tenant can connect more than one cloud provider. The tenant/provider
            // pair is enforced by the unique index; each connection needs its own PK.
            Id = tenantId,
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

    public Guid InitiatedByUserId { get; private set; }
    public DateTime ExpiresAtUtc { get; private set; }

    public static CloudProviderOAuthState Create(Guid tenantId, Guid userId)
    {
        var state = new CloudProviderOAuthState { Id = tenantId, InitiatedByUserId = userId, ExpiresAtUtc = DateTime.UtcNow.AddMinutes(10) };
        state.SetTenant(tenantId);
        return state;
    }
}

public sealed class TenantCloudFileCopy : TenantEntity
{
    private TenantCloudFileCopy() { }
    public Guid FileId { get; private set; }
    public string Provider { get; private set; } = "google-drive";
    public string RemoteFileId { get; private set; } = string.Empty;
    public string ChecksumSha256 { get; private set; } = string.Empty;

    public static TenantCloudFileCopy Create(Guid tenantId, Guid fileId, string remoteFileId, string checksum)
        => Create(tenantId, fileId, "google-drive", remoteFileId, checksum);

    public static TenantCloudFileCopy Create(Guid tenantId, Guid fileId, string provider, string remoteFileId, string checksum)
    {
        var copy = new TenantCloudFileCopy { FileId = fileId, Provider = provider, RemoteFileId = remoteFileId, ChecksumSha256 = checksum };
        copy.SetTenant(tenantId);
        return copy;
    }
}
