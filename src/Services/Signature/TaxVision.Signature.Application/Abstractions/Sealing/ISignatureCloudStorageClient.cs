using BuildingBlocks.Results;

namespace TaxVision.Signature.Application.Abstractions.Sealing;

public sealed record SignatureFileUpload(
    byte[] Content,
    string FileName,
    string ContentType,
    string OwnerType,
    Guid OwnerId,
    string FolderType,
    int? TaxYear,
    Guid ActorId
);

/// <summary>
/// Metadata mínima de un file en CloudStorage; la proyección local se puede regenerar desde esto
/// cuando el bus (FileAvailable) la dejó stale.
/// </summary>
public sealed record SignatureFileMetadata(
    Guid Id,
    string Status,
    string? ChecksumSha256,
    string? ContentType,
    long SizeBytes,
    string? ObjectKey,
    // P-18: owner/carpeta reales en CloudStorage, para validar que la copia a firmar es Signature/Signatures.
    string? OwnerType = null,
    string? FolderType = null
);

/// <summary>
/// Cliente del microservicio CloudStorage acotado a lo que necesita el sealing worker:
/// descargar el PDF original por FileId y subir el sellado + certificate como archivos
/// nuevos. No expone flujos ajenos al sealing (search, quotas, etc.).
///
/// <para>
/// Fase D0/D1 — DownloadAsync sigue via HTTP+M2M (fuera del scope de D1, ver
/// project_cloudstorage_hardening_plan.md): el token lo toma
/// <see cref="ISignatureServiceTokenAcquirer"/>, igual que Notification. UploadAsync en
/// cambio ya no llama a CloudStorage por HTTP — sube el objeto directo a MinIO con
/// credenciales propias (IAM scoped) y publica SaveFileRequestedIntegrationEvent; el
/// TenantId se pasa explícito porque este cliente se llama desde un consumer background
/// sin request context.
/// </para>
/// </summary>
public interface ISignatureCloudStorageClient
{
    /// <summary>Descarga los bytes del PDF original.</summary>
    Task<Result<byte[]>> DownloadAsync(Guid tenantId, Guid fileId, CancellationToken ct = default);

    /// <summary>Sube un PDF a CloudStorage y devuelve el nuevo <c>FileId</c>.</summary>
    Task<Result<Guid>> UploadAsync(Guid tenantId, SignatureFileUpload upload, CancellationToken ct = default);

    /// <summary>
    /// URL firmada de corta vida (adjunto) para que el navegador del firmante baje un archivo de su entrega.
    /// Signature la pide con su token M2M en el momento de abrir el enlace; no crea ningún share.
    /// </summary>
    Task<Result<Uri>> GetDownloadUrlAsync(Guid tenantId, Guid fileId, CancellationToken ct = default);

    /// <summary>
    /// Copia en servidor (sin bajar bytes) de un archivo final a la carpeta "Signed Documents" del portal de
    /// un cliente (owner Customer, FolderType Signatures). CloudStorage la escanea como cualquier archivo.
    /// </summary>
    Task<Result<Guid>> CopyToCustomerAsync(
        Guid tenantId,
        Guid sourceFileId,
        Guid customerId,
        int taxYear,
        CancellationToken ct = default
    );

    /// <summary>
    /// GET /storage/files/{id}: fuente autoritaria del estado real del file. Se usa como fallback
    /// cuando la proyección local está vacía o stale — p.ej. tras un restart de Signature se perdió
    /// un <c>FileAvailable</c>, o la proyección quedó <c>Deleted</c> por un evento ruidoso.
    /// </summary>
    Task<Result<SignatureFileMetadata>> GetFileAsync(Guid tenantId, Guid fileId, CancellationToken ct = default);
}
