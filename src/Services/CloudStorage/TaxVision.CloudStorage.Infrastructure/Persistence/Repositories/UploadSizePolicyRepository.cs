using Microsoft.EntityFrameworkCore;
using TaxVision.CloudStorage.Application.Quotas;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.Quotas;

namespace TaxVision.CloudStorage.Infrastructure.Persistence.Repositories;

/// <summary>Sin IgnoreQueryFilters: la entidad no es tenant-owned, así que el filtro fail-closed no la alcanza.</summary>
public sealed class UploadSizePolicyRepository(CloudStorageDbContext db) : IUploadSizePolicyRepository
{
    public void Add(UploadSizePolicy policy) => db.UploadSizePolicies.Add(policy);

    public void Remove(UploadSizePolicy policy) => db.UploadSizePolicies.Remove(policy);

    public Task<UploadSizePolicy?> GetAsync(
        UploadSizePolicyScope scope,
        FolderType folderType,
        string? planCode,
        Guid? tenantId,
        CancellationToken ct
    ) =>
        db.UploadSizePolicies.SingleOrDefaultAsync(
            p => p.Scope == scope && p.FolderType == folderType && p.PlanCode == planCode && p.TenantId == tenantId,
            ct
        );

    public async Task<IReadOnlyList<UploadSizePolicy>> ListAsync(CancellationToken ct) =>
        await db
            .UploadSizePolicies.AsNoTracking()
            .OrderBy(p => p.FolderType)
            .ThenBy(p => p.Scope)
            .ThenBy(p => p.PlanCode)
            .ThenBy(p => p.TenantId)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<UploadSizePolicy>> ListApplicableAsync(
        FolderType folderType,
        string planCode,
        Guid tenantId,
        CancellationToken ct
    ) =>
        await db
            .UploadSizePolicies.AsNoTracking()
            .Where(p =>
                p.FolderType == folderType
                && (
                    p.Scope == UploadSizePolicyScope.Global
                    || (p.Scope == UploadSizePolicyScope.Plan && p.PlanCode == planCode)
                    || (p.Scope == UploadSizePolicyScope.Tenant && p.TenantId == tenantId)
                )
            )
            .ToListAsync(ct);
}

public sealed class UploadSizePolicyAuditRepository(CloudStorageDbContext db) : IUploadSizePolicyAuditRepository
{
    public void Add(UploadSizePolicyAudit entry) => db.UploadSizePolicyAudits.Add(entry);

    public async Task<IReadOnlyList<UploadSizePolicyAudit>> ListAsync(int skip, int take, CancellationToken ct) =>
        await db
            .UploadSizePolicyAudits.AsNoTracking()
            .OrderByDescending(e => e.OccurredAtUtc)
            .ThenByDescending(e => e.Id)
            .Skip(skip)
            .Take(take)
            .ToListAsync(ct);
}
