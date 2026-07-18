using BuildingBlocks.Results;

namespace TaxVision.Correspondence.Application.Abstractions;

/// <summary>
/// Sube un attachment recién bajado de Connectors al bucket temporal de MinIO
/// (<c>taxvision-temp/correspondence/*</c>) con credenciales propias de Correspondence — mismo
/// patrón D0/D1 que reemplazó el flujo HTTP+M2M initiate/PUT/complete en Signature (ver
/// <c>SignatureCloudStorageClient.UploadAsync</c>). A propósito NO publica
/// <c>SaveFileRequestedIntegrationEvent</c> acá — eso lo hace <c>DownloadAttachmentHandler</c>
/// como un paso separado (SRP: subir bytes y anunciar el archivo son responsabilidades distintas).
/// </summary>
public interface ICorrespondenceTempBucketUploader
{
    Task<Result<TempBucketUploadResult>> UploadAsync(
        Guid fileId,
        byte[] content,
        string filename,
        string contentType,
        CancellationToken ct = default
    );
}

/// <summary>Ubicación del objeto recién subido dentro del bucket temporal — insumo directo de <c>SaveFileRequestedIntegrationEvent</c>.</summary>
public sealed record TempBucketUploadResult(string SourceBucket, string SourceObjectKey);
