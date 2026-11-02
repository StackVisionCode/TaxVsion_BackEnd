using Microsoft.EntityFrameworkCore;
using TaxVision.CloudStorage.Application.Quotas;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.FileTypes;

namespace TaxVision.CloudStorage.Infrastructure.Persistence.Repositories;

public sealed class FilePolicyCatalogRepository(CloudStorageDbContext db) : IFilePolicyCatalogRepository
{
    public async Task<IReadOnlyList<FileTypeDefinition>> ListFileTypesAsync(CancellationToken ct) =>
        await db
            .FileTypeDefinitions.AsNoTracking()
            .OrderBy(definition => definition.Group)
            .ThenBy(definition => definition.Extension)
            .ToListAsync(ct);

    public Task<FileTypeDefinition?> GetFileTypeAsync(string extension, CancellationToken ct) =>
        db.FileTypeDefinitions.SingleOrDefaultAsync(definition => definition.Extension == extension, ct);

    public void AddFileType(FileTypeDefinition definition) => db.FileTypeDefinitions.Add(definition);

    public async Task<IReadOnlyList<FolderFilePolicy>> ListFolderPoliciesAsync(CancellationToken ct) =>
        await db.FolderFilePolicies.AsNoTracking().ToListAsync(ct);

    public Task<FolderFilePolicy?> GetFolderPolicyAsync(FolderType folderType, CancellationToken ct) =>
        db.FolderFilePolicies.SingleOrDefaultAsync(policy => policy.FolderType == folderType, ct);

    public void AddAudit(FilePolicyAudit audit) => db.FilePolicyAudits.Add(audit);
}
