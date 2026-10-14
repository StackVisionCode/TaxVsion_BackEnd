using Microsoft.EntityFrameworkCore;
using TaxVision.CloudStorage.Application.CustomerAccess;
using TaxVision.CloudStorage.Domain.CustomerAccess;

namespace TaxVision.CloudStorage.Infrastructure.Persistence.Repositories;

public sealed class CustomerAccessRuleRepository(CloudStorageDbContext db) : ICustomerAccessRuleRepository
{
    public void Add(CustomerDocumentAccessRule rule) => db.CustomerDocumentAccessRules.Add(rule);

    public void Remove(CustomerDocumentAccessRule rule) => db.CustomerDocumentAccessRules.Remove(rule);

    public Task<CustomerDocumentAccessRule?> GetAsync(
        Guid tenantId,
        Guid customerId,
        AccessResourceType resourceType,
        Guid resourceId,
        AccessCapability capability,
        CancellationToken ct
    ) =>
        db
            .CustomerDocumentAccessRules.IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                r =>
                    r.TenantId == tenantId
                    && r.CustomerId == customerId
                    && r.ResourceType == resourceType
                    && r.ResourceId == resourceId
                    && r.Capability == capability,
                ct
            );

    public async Task<IReadOnlyList<CustomerDocumentAccessRule>> ListByCustomerAsync(
        Guid tenantId,
        Guid customerId,
        CancellationToken ct
    ) =>
        await db
            .CustomerDocumentAccessRules.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(r => r.TenantId == tenantId && r.CustomerId == customerId)
            .OrderBy(r => r.ResourceType)
            .ThenBy(r => r.ResourceId)
            .ThenBy(r => r.Capability)
            .ToListAsync(ct);
}

public sealed class CustomerAccessRuleAuditRepository(CloudStorageDbContext db) : ICustomerAccessRuleAuditRepository
{
    public void Add(CustomerAccessRuleAudit entry) => db.CustomerAccessRuleAudits.Add(entry);
}
