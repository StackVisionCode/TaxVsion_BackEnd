using TaxVision.CloudStorage.Application.Configuration;
using TaxVision.CloudStorage.Domain.Files;
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
/// Política efectiva de un upload: tipos permitidos de <see cref="CloudStorageOptions.ResolveUploadPolicy"/>
/// y tamaño de la cadena en BD (Tenant > Plan > Global), cayendo al valor de código si no hay filas.
/// </summary>
public interface IUploadPolicyResolver
{
    Task<EffectiveUploadPolicy> ResolveAsync(
        Guid tenantId,
        string planCode,
        FolderType folderType,
        CancellationToken ct
    );
}
