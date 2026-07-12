using Microsoft.EntityFrameworkCore;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Projections;

namespace TaxVision.Signature.Infrastructure.Persistence.Repositories;

internal sealed class FileMetadataRefRepository(SignatureDbContext db) : IFileMetadataRefRepository
{
    public Task<FileMetadataRef?> GetByFileIdAsync(Guid tenantId, Guid fileId, CancellationToken ct = default) =>
        db.FileMetadataRefs.FirstOrDefaultAsync(p => p.TenantId == tenantId && p.FileId == fileId, ct);

    public async Task AddAsync(FileMetadataRef projection, CancellationToken ct = default)
    {
        await db.FileMetadataRefs.AddAsync(projection, ct);
    }
}
