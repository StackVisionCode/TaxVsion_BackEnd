using BuildingBlocks.Domain;

namespace TaxVision.CloudStorage.Domain.Providers;

/// <summary>Conexión OAuth de almacenamiento asociada exclusivamente a un tenant.</summary>
public sealed class TenantCloudConnection : TenantEntity
{
    private TenantCloudConnection() { }

    public const int ReviewReasonMaxLength = 64;

    public string Provider { get; private set; } = "google-drive";
    public string AccountEmail { get; private set; } = string.Empty;
    public string ProtectedRefreshToken { get; private set; } = string.Empty;
    public DateTime ConnectedAtUtc { get; private set; }

    /// <summary>Quién autorizó la conexión (null en las anteriores a guardarlo). Solo informativo: la conexión es de la oficina.</summary>
    [UserReference(
        UserReferenceKind.Credential,
        HandledBy = "CloudConnectionsUserOffboardedConsumer",
        Note = "No se revoca: se marca para revisión y se avisa."
    )]
    public Guid? ConnectedByUserId { get; private set; }

    /// <summary>Desde cuándo la oficina debe revisarla (p. ej. la conectó alguien que ya no está). Reconectar crea otra fila y la limpia.</summary>
    public DateTime? ReviewRequiredAtUtc { get; private set; }

    /// <summary>Motivo de la revisión; ver <see cref="CloudConnectionReviewReasons"/>.</summary>
    public string? ReviewReason { get; private set; }

    public bool NeedsReview => ReviewRequiredAtUtc is not null;

    public void UpdateProtectedRefreshToken(string protectedRefreshToken) =>
        ProtectedRefreshToken = protectedRefreshToken;

    public static TenantCloudConnection Connect(Guid tenantId, string email, string protectedRefreshToken) =>
        Connect(tenantId, "google-drive", email, protectedRefreshToken);

    public static TenantCloudConnection Connect(
        Guid tenantId,
        string provider,
        string email,
        string protectedRefreshToken,
        Guid? connectedByUserId = null
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
            ConnectedByUserId = connectedByUserId is { } userId && userId != Guid.Empty ? userId : null,
        };
        connection.SetTenant(tenantId);
        return connection;
    }

    /// <summary>
    /// Por qué esta conexión queda ligada a una persona que se retira: la cuenta es la suya (mismo email,
    /// sin distinguir mayúsculas) o fue quien la autorizó. Null = no está ligada.
    /// </summary>
    public string? LinkedReasonFor(Guid userId, string? email)
    {
        if (
            !string.IsNullOrWhiteSpace(email)
            && string.Equals(AccountEmail, email.Trim(), StringComparison.OrdinalIgnoreCase)
        )
            return CloudConnectionReviewReasons.AccountOfOffboardedUser;
        return userId != Guid.Empty && ConnectedByUserId == userId
            ? CloudConnectionReviewReasons.ConnectedByOffboardedUser
            : null;
    }

    /// <summary>Marca la conexión para revisión. Idempotente: si ya estaba marcada no cambia nada y devuelve false.</summary>
    public bool RequireReview(string reason, DateTime nowUtc)
    {
        if (ReviewRequiredAtUtc is not null)
            return false;
        ReviewRequiredAtUtc = nowUtc;
        ReviewReason = reason.Length > ReviewReasonMaxLength ? reason[..ReviewReasonMaxLength] : reason;
        return true;
    }
}

/// <summary>Motivos de <see cref="TenantCloudConnection.ReviewReason"/>.</summary>
public static class CloudConnectionReviewReasons
{
    /// <summary>La cuenta conectada es la de un empleado retirado (mismo email).</summary>
    public const string AccountOfOffboardedUser = "AccountOfOffboardedUser";

    /// <summary>La autorizó un empleado retirado con una cuenta distinta a la suya.</summary>
    public const string ConnectedByOffboardedUser = "ConnectedByOffboardedUser";
}

/// <summary>Nonce OAuth de un solo uso; tenant/usuario nunca se aceptan desde el callback.</summary>
public sealed class CloudProviderOAuthState : TenantEntity
{
    private CloudProviderOAuthState() { }

    public const int ReturnOriginMaxLength = 255;

    [UserReference(
        UserReferenceKind.Credential,
        HandledBy = "CloudConnectionsUserOffboardedConsumer",
        Note = "Los intentos de conexión pendientes se descartan."
    )]
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
