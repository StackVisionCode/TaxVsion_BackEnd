using TaxVision.CloudStorage.Application.Quotas;
using TaxVision.CloudStorage.Domain.Files;
using TaxVision.CloudStorage.Domain.Quotas;

namespace TaxVision.CloudStorage.Tests.Application;

internal sealed class FakeUploadSizePolicyRepository : IUploadSizePolicyRepository
{
    private readonly Dictionary<Guid, UploadSizePolicy> _byId = [];

    public int ApplicableReads { get; private set; }

    public IReadOnlyCollection<UploadSizePolicy> All() => _byId.Values.ToList();

    public void Seed(UploadSizePolicy policy) => _byId[policy.Id] = policy;

    public void Add(UploadSizePolicy policy) => _byId[policy.Id] = policy;

    public void Remove(UploadSizePolicy policy) => _byId.Remove(policy.Id);

    public Task<UploadSizePolicy?> GetAsync(
        UploadSizePolicyScope scope,
        FolderType folderType,
        string? planCode,
        Guid? tenantId,
        CancellationToken ct
    ) =>
        Task.FromResult(
            _byId.Values.SingleOrDefault(p =>
                p.Scope == scope
                && p.FolderType == folderType
                && string.Equals(p.PlanCode, planCode, StringComparison.OrdinalIgnoreCase)
                && p.TenantId == tenantId
            )
        );

    public Task<IReadOnlyList<UploadSizePolicy>> ListAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<UploadSizePolicy>>(_byId.Values.ToList());

    public Task<IReadOnlyList<UploadSizePolicy>> ListApplicableAsync(
        FolderType folderType,
        string planCode,
        Guid tenantId,
        CancellationToken ct
    )
    {
        ApplicableReads++;
        return Task.FromResult<IReadOnlyList<UploadSizePolicy>>(
            _byId
                .Values.Where(p =>
                    p.FolderType == folderType
                    && (
                        p.Scope == UploadSizePolicyScope.Global
                        || (
                            p.Scope == UploadSizePolicyScope.Plan
                            && string.Equals(p.PlanCode, planCode, StringComparison.OrdinalIgnoreCase)
                        )
                        || (p.Scope == UploadSizePolicyScope.Tenant && p.TenantId == tenantId)
                    )
                )
                .ToList()
        );
    }
}

internal sealed class FakeUploadSizePolicyAuditRepository : IUploadSizePolicyAuditRepository
{
    public List<UploadSizePolicyAudit> Entries { get; } = [];

    public void Add(UploadSizePolicyAudit entry) => Entries.Add(entry);

    public Task<IReadOnlyList<UploadSizePolicyAudit>> ListAsync(int skip, int take, CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<UploadSizePolicyAudit>>(
            Entries.OrderByDescending(e => e.OccurredAtUtc).Skip(skip).Take(take).ToList()
        );
}
