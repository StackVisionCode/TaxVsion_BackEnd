using BuildingBlocks.Results;

namespace TaxVision.Tenant.Application.Tenants.Abstractions;

/// <summary>Bytes de un logo a subir — validados por UploadTenantLogoHandler antes de llegar acá.</summary>
public sealed record TenantLogoUpload(byte[] Content, string ContentType, string FileName, Guid ActorId);

public sealed record TenantLogoDownloadUrl(Uri Url, DateTime ExpiresAtUtc);

/// <summary>
/// Cliente de CloudStorage para el logo del tenant — mismo patrón "Fase D1" ya usado por
/// Signature/Customer: <see cref="UploadAsync"/> sube directo a MinIO con credenciales propias
/// (IAM scoped a taxvision-temp/tenant-branding/*) y publica SaveFileRequestedIntegrationEvent para
/// que CloudStorage lo catalogue/escanee de forma asincrona. Download y Delete siguen el flujo
/// HTTP+M2M presignado normal.
/// </summary>
public interface ITenantBrandingCloudStorageClient
{
    Task<Result<Guid>> UploadAsync(Guid tenantId, TenantLogoUpload upload, CancellationToken ct = default);

    Task<Result<TenantLogoDownloadUrl>> GetDownloadUrlAsync(Guid tenantId, Guid fileId, CancellationToken ct = default);

    Task<Result> DeleteAsync(Guid tenantId, Guid fileId, CancellationToken ct = default);
}
