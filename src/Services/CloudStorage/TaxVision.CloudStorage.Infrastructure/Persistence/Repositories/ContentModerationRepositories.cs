using Microsoft.EntityFrameworkCore;
using TaxVision.CloudStorage.Application.Moderation;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.Moderation;

namespace TaxVision.CloudStorage.Infrastructure.Persistence.Repositories;

/// <summary>La política no es tenant-owned: el filtro fail-closed no la alcanza.</summary>
public sealed class ContentModerationPolicyRepository(CloudStorageDbContext db) : IContentModerationPolicyRepository
{
    public Task<ContentModerationPolicy?> GetAsync(CancellationToken ct) =>
        db.ContentModerationPolicies.SingleOrDefaultAsync(p => p.Id == ContentModerationPolicy.PlatformPolicyId, ct);
}

public sealed class ContentModerationPolicyAuditRepository(CloudStorageDbContext db)
    : IContentModerationPolicyAuditRepository
{
    public void Add(ContentModerationPolicyAudit entry) => db.ContentModerationPolicyAudits.Add(entry);

    public async Task<IReadOnlyList<ContentModerationPolicyAudit>> ListAsync(
        int skip,
        int take,
        CancellationToken ct
    ) =>
        await db
            .ContentModerationPolicyAudits.AsNoTracking()
            .OrderByDescending(e => e.OccurredAtUtc)
            .ThenByDescending(e => e.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);
}

/// <summary>Cola de revisión de plataforma: cross-tenant a propósito, IgnoreQueryFilters() intencional.</summary>
public sealed class ContentReviewQueue(CloudStorageDbContext db) : IContentReviewQueue
{
    public async Task<IReadOnlyList<FileObject>> ListAsync(
        FileStatus status,
        int skip,
        int take,
        CancellationToken ct
    ) =>
        await db
            .Files.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(file => file.Status == status)
            .OrderBy(file => file.ScannedAtUtc)
            .ThenBy(file => file.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);
}
