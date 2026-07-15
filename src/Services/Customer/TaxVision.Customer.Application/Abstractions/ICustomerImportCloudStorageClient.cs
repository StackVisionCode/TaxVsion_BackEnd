using BuildingBlocks.Results;

namespace TaxVision.Customer.Application.Abstractions;

/// <summary>
/// Cliente del microservicio CloudStorage acotado a lo que necesita el import masivo de
/// customers: subir el archivo (Excel/CSV) sin pasar los bytes por el backend, descargarlo
/// de vuelta para parsear filas, y borrarlo cuando el import termina.
///
/// <para>
/// UploadAsync sube el objeto directo a MinIO con credenciales propias de Customer (IAM
/// scoped a taxvision-temp/customer/*, ver deploy/docker/minio/policies/customer-source.json)
/// y publica SaveFileRequestedIntegrationEvent para que CloudStorage lo registre y escanee
/// de forma asincrona — mismo patron que Signature/Notification (Fase D). El fileId lo
/// dicta el llamador (reusa CustomerImportAttempt.Id) para poder correlacionar el resultado
/// del escaneo sin persistir un campo aparte. DownloadAsync/DeleteAsync siguen via
/// HTTP+M2M (token client-credentials contra Auth).
/// </para>
/// </summary>
public interface ICustomerImportCloudStorageClient
{
    Task<Result> UploadAsync(
        Guid tenantId,
        Guid fileId,
        byte[] content,
        string fileName,
        string contentType,
        Guid actorId,
        CancellationToken ct = default
    );

    /// <summary>Descarga los bytes del archivo de import ya escaneado y disponible.</summary>
    Task<Result<byte[]>> DownloadAsync(Guid tenantId, Guid fileId, CancellationToken ct = default);

    /// <summary>Borra (soft-delete) el archivo de import en CloudStorage al terminar de procesarlo.</summary>
    Task<Result> DeleteAsync(Guid tenantId, Guid fileId, CancellationToken ct = default);
}
