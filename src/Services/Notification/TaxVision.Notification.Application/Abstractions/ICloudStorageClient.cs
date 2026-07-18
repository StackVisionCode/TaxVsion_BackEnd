using BuildingBlocks.Results;

namespace TaxVision.Notification.Application.Abstractions;

/// <summary>Contenido a subir a CloudStorage (el servicio genera la object key y aplica escaneo antivirus).</summary>
public sealed record CloudStorageUpload(
    byte[] Content,
    string OriginalName,
    string ContentType,
    string OwnerType,
    Guid? OwnerId,
    string FolderType,
    int? TaxYear
);

/// <summary>
/// Cliente del microservicio CloudStorage (HTTP a <c>FilesController</c>). Notification no accede a
/// MinIO ni a la BD de CloudStorage: solo guarda FileIds y URLs presignadas. El flujo de subida es
/// asíncrono (initiate → PUT presignado → complete → escaneo).
/// El parámetro <c>tenantId</c> se usa SOLO en contexto background para obtener un token de servicio
/// (M2M) del tenant; en contexto request se reenvía el token del usuario y se ignora.
/// </summary>
public interface ICloudStorageClient
{
    Task<Result<Guid>> UploadAsync(CloudStorageUpload upload, Guid? tenantId = null, CancellationToken ct = default);

    Task<Result<string>> DownloadTextAsync(Guid fileId, Guid? tenantId = null, CancellationToken ct = default);

    Task<Result<Uri>> GetDownloadUrlAsync(Guid fileId, Guid? tenantId = null, CancellationToken ct = default);
}

/// <summary>
/// Provee el bearer token para autenticar la llamada saliente a CloudStorage. En contexto de request
/// HTTP reenvía el token del usuario; en contexto background usa un token de servicio (M2M) del tenant
/// indicado. Devuelve null si no hay token disponible.
/// </summary>
public interface ICloudStorageTokenProvider
{
    Task<string?> GetTokenAsync(Guid? tenantId, CancellationToken ct = default);
}

/// <summary>Obtiene (y cachea) tokens de servicio M2M del Auth para un tenant (grant client-credentials).</summary>
public interface IServiceTokenAcquirer
{
    Task<string?> GetTokenAsync(Guid tenantId, CancellationToken ct = default);
}
