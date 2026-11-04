using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.FileTypes;

namespace TaxVision.CloudStorage.Application.Quotas;

/// <summary>Catálogo de tipos de archivo y políticas por carpeta: datos de la plataforma, sin tenant.</summary>
public interface IFilePolicyCatalogRepository
{
    Task<IReadOnlyList<FileTypeDefinition>> ListFileTypesAsync(CancellationToken ct);
    Task<FileTypeDefinition?> GetFileTypeAsync(string extension, CancellationToken ct);
    void AddFileType(FileTypeDefinition definition);

    Task<IReadOnlyList<FolderFilePolicy>> ListFolderPoliciesAsync(CancellationToken ct);
    Task<FolderFilePolicy?> GetFolderPolicyAsync(FolderType folderType, CancellationToken ct);

    void AddAudit(FilePolicyAudit audit);
}

/// <summary>Instantánea del catálogo y de las políticas por carpeta (lo que el resolutor necesita).</summary>
public sealed record FilePolicyCatalog(
    IReadOnlyList<FileTypeDefinition> FileTypes,
    IReadOnlyDictionary<FolderType, FolderFilePolicy> FolderPolicies
);

/// <summary>
/// Caché por réplica del catálogo (60 s). Las escrituras de PlatformAdmin llaman <see cref="Invalidate"/>;
/// las demás réplicas lo ven al vencer el TTL.
/// </summary>
public sealed class FilePolicyCatalogCache
{
    public static readonly TimeSpan TimeToLive = TimeSpan.FromSeconds(60);

    private (FilePolicyCatalog Catalog, DateTime ExpiresAtUtc)? _entry;

    public bool TryGet(DateTime nowUtc, out FilePolicyCatalog catalog)
    {
        if (_entry is { } entry && entry.ExpiresAtUtc > nowUtc)
        {
            catalog = entry.Catalog;
            return true;
        }
        catalog = default!;
        return false;
    }

    public void Set(FilePolicyCatalog catalog, DateTime nowUtc) => _entry = (catalog, nowUtc + TimeToLive);

    public void Invalidate() => _entry = null;
}
