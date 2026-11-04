using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.FileTypes;
using TaxVision.CloudStorage.Domain.Quotas;

namespace TaxVision.CloudStorage.Application.Quotas;

/// <summary>Filas de tope de tamaño (Global/Plan/Tenant). Cross-tenant por diseño: la entidad no es tenant-owned.</summary>
public interface IUploadSizePolicyRepository
{
    void Add(UploadSizePolicy policy);
    void Remove(UploadSizePolicy policy);

    /// <summary>Lookup por la clave única (scope, folderType, planCode, tenantId) — null si no hay fila.</summary>
    Task<UploadSizePolicy?> GetAsync(
        UploadSizePolicyScope scope,
        FolderType folderType,
        string? planCode,
        Guid? tenantId,
        CancellationToken ct
    );

    Task<IReadOnlyList<UploadSizePolicy>> ListAsync(CancellationToken ct);

    /// <summary>Las (a lo sumo 3) filas que aplican a un upload: Global, Plan del tenant y Tenant, todas del mismo FolderType.</summary>
    Task<IReadOnlyList<UploadSizePolicy>> ListApplicableAsync(
        FolderType folderType,
        string planCode,
        Guid tenantId,
        CancellationToken ct
    );
}

public interface IUploadSizePolicyAuditRepository
{
    void Add(UploadSizePolicyAudit entry);
    Task<IReadOnlyList<UploadSizePolicyAudit>> ListAsync(int skip, int take, CancellationToken ct);
}

/// <summary>
/// Política efectiva de un upload (tamaño y tipos) para un tenant y una carpeta: catálogo y política por
/// carpeta de la plataforma, lo que vende el plan, las excepciones de tamaño en BD (Tenant > Plan > Global)
/// y, si sube un cliente del portal, la restricción de su oficina.
/// </summary>
public interface IUploadPolicyResolver
{
    Task<EffectiveUploadPolicy> ResolveAsync(
        Guid tenantId,
        TenantStorageLimit limit,
        FolderType folderType,
        bool forCustomerPortal,
        CancellationToken ct
    );
}
