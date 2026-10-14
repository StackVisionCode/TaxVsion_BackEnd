using BuildingBlocks.CustomerVisibility;
using TaxVision.CloudStorage.Application.CustomerAccess;
using TaxVision.CloudStorage.Domain.CustomerAccess;

namespace TaxVision.CloudStorage.Tests.CustomerAccess;

internal sealed class FakeCustomerAccessRuleRepository : ICustomerAccessRuleRepository
{
    private readonly Dictionary<Guid, CustomerDocumentAccessRule> _byId = [];

    public IReadOnlyCollection<CustomerDocumentAccessRule> All() => _byId.Values.ToList();

    public void Add(CustomerDocumentAccessRule rule) => _byId[rule.Id] = rule;

    public void Remove(CustomerDocumentAccessRule rule) => _byId.Remove(rule.Id);

    public Task<CustomerDocumentAccessRule?> GetAsync(
        Guid tenantId,
        Guid customerId,
        AccessResourceType resourceType,
        Guid resourceId,
        AccessCapability capability,
        CancellationToken ct
    ) =>
        Task.FromResult(
            _byId.Values.SingleOrDefault(r =>
                r.TenantId == tenantId
                && r.CustomerId == customerId
                && r.ResourceType == resourceType
                && r.ResourceId == resourceId
                && r.Capability == capability
            )
        );

    public Task<IReadOnlyList<CustomerDocumentAccessRule>> ListByCustomerAsync(
        Guid tenantId,
        Guid customerId,
        CancellationToken ct
    ) =>
        Task.FromResult<IReadOnlyList<CustomerDocumentAccessRule>>(
            _byId.Values.Where(r => r.TenantId == tenantId && r.CustomerId == customerId).ToList()
        );
}

internal sealed class FakeCustomerAccessRuleAuditRepository : ICustomerAccessRuleAuditRepository
{
    public List<CustomerAccessRuleAudit> Entries { get; } = [];

    public void Add(CustomerAccessRuleAudit entry) => Entries.Add(entry);
}

internal sealed class FakeCustomerAssignmentProjectionStore : ICustomerAssignmentProjectionStore
{
    private readonly HashSet<(Guid tenantId, Guid customerId, Guid userId)> _assignments = [];

    public FakeCustomerAssignmentProjectionStore Assign(Guid tenantId, Guid customerId, Guid userId)
    {
        _assignments.Add((tenantId, customerId, userId));
        return this;
    }

    public Task<DateTime?> GetVersionAsync(Guid tenantId, Guid customerId, CancellationToken ct = default) =>
        Task.FromResult<DateTime?>(null);

    public Task<bool> IsAnyAssignedToUserAsync(
        Guid tenantId,
        Guid userId,
        IReadOnlyCollection<Guid> customerIds,
        CancellationToken ct = default
    ) => Task.FromResult(customerIds.Any(c => _assignments.Contains((tenantId, c, userId))));

    public Task<IReadOnlyCollection<Guid>> GetAssignedCustomerIdsAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken ct = default
    ) =>
        Task.FromResult<IReadOnlyCollection<Guid>>(
            _assignments.Where(a => a.tenantId == tenantId && a.userId == userId).Select(a => a.customerId).ToList()
        );

    public Task ReplaceAsync(
        Guid tenantId,
        Guid customerId,
        IReadOnlyCollection<Guid> userIds,
        DateTime version,
        CancellationToken ct = default
    ) => Task.CompletedTask;
}
