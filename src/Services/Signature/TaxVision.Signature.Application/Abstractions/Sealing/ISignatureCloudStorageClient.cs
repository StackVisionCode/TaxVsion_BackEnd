using BuildingBlocks.Results;

namespace TaxVision.Signature.Application.Abstractions.Sealing;

public sealed record SignaturePdfUpload(
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
    Task<Result<Guid>> UploadAsync(Guid tenantId, SignaturePdfUpload upload, CancellationToken ct = default);
}
