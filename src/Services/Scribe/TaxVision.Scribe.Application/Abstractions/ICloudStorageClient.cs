using BuildingBlocks.Results;

namespace TaxVision.Scribe.Application.Abstractions;

/// <summary>
/// Cliente de CloudStorage para Scribe. La lectura (Fase 4) sigue el flujo HTTP+M2M presignado; la
/// escritura (Fase 5) sube directo a MinIO con credenciales propias de Scribe y publica
/// <c>SaveFileRequestedIntegrationEvent</c> — mismo patrón D1 que Signature/Customer/Notification.
/// Uso: <see cref="TaxVision.Scribe.Application.Templates.Storage.ITemplateStorageService"/>, que le
/// aplica las convenciones de OwnerType/FolderType propias de templates/layouts.
/// </summary>
public interface ICloudStorageClient
{
    Task<Result<string>> DownloadTextAsync(Guid fileId, Guid? tenantId, CancellationToken ct = default);

    /// <summary>
    /// Sube <paramref name="content"/> al bucket temporal y publica el evento de escaneo. El FileId lo
    /// genera el llamador (idempotencia — un redelivery con el mismo FileId es no-op en CloudStorage).
    /// Siempre OwnerType=Tenant: Scribe nunca sube contenido dueño de un Customer/Signature/etc., y para
    /// templates System usa <see cref="BuildingBlocks.Tenancy.PlatformTenant.Id"/> como TenantId (mismo
    /// criterio que <see cref="DownloadTextAsync"/>). Devuelve el SourceObjectKey usado en el bucket
    /// temporal (guardado como StorageKey en el dominio — es el identificador legible/auditable; el
    /// FileId sigue siendo lo único necesario para recuperarlo).
    /// </summary>
    Task<Result<string>> UploadAsync(
        Guid? tenantId,
        Guid fileId,
        byte[] content,
        string fileName,
        string contentType,
        string folderType,
        Guid actorId,
        CancellationToken ct = default
    );
}

/// <summary>Obtiene (y cachea) tokens de servicio M2M del Auth para un tenant (grant client-credentials).</summary>
public interface IServiceTokenAcquirer
{
    Task<string?> GetTokenAsync(Guid tenantId, CancellationToken ct = default);
}
