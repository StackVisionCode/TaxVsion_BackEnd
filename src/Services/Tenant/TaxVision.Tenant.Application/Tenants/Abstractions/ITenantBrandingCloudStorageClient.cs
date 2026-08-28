using BuildingBlocks.Results;

namespace TaxVision.Tenant.Application.Tenants.Abstractions;

/// <summary>Bytes de un asset a subir — validados por UploadTenantBrandAssetHandler antes de llegar acá.</summary>
public sealed record TenantLogoUpload(byte[] Content, string ContentType, string FileName, Guid ActorId);

public sealed record TenantLogoDownloadUrl(Uri Url, DateTime ExpiresAtUtc);

/// <summary>Archivo ya subido a MinIO, aún sin pedir el catálogo/escaneo a CloudStorage.</summary>
public sealed record TenantBrandStoredFile(Guid FileId, string SourceObjectKey);

/// <summary>
/// Cliente de CloudStorage para el logo del tenant — mismo patrón "Fase D1" ya usado por
/// Signature/Customer: <see cref="UploadAsync"/> sube directo a MinIO con credenciales propias
/// (IAM scoped a taxvision-temp/tenant-branding/*) y publica SaveFileRequestedIntegrationEvent para
/// que CloudStorage lo catalogue/escanee de forma asincrona. Download y Delete siguen el flujo
/// HTTP+M2M presignado normal.
/// </summary>
public interface ITenantBrandingCloudStorageClient
{
    /// <summary>Solo sube a MinIO y devuelve el fileId — NO pide el escaneo todavía. Permite al
    /// handler persistir el asset Pending ANTES de disparar el escaneo (evita que el resultado del
    /// escaneo llegue antes de que exista la fila y quede huérfana en Pending).</summary>
    Task<Result<TenantBrandStoredFile>> StoreAsync(
        Guid tenantId,
        TenantLogoUpload upload,
        CancellationToken ct = default
    );

    /// <summary>Publica SaveFileRequested para que CloudStorage catalogue y escanee el archivo ya
    /// subido. Se llama DESPUÉS de guardar el asset Pending.</summary>
    Task RequestCatalogAsync(
        Guid tenantId,
        TenantLogoUpload upload,
        TenantBrandStoredFile stored,
        CancellationToken ct = default
    );

    Task<Result<TenantLogoDownloadUrl>> GetDownloadUrlAsync(Guid tenantId, Guid fileId, CancellationToken ct = default);

    Task<Result> DeleteAsync(Guid tenantId, Guid fileId, CancellationToken ct = default);
}
