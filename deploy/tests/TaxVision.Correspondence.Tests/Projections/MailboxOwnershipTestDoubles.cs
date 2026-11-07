using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Domain.Projections;

namespace TaxVision.Correspondence.Tests.Projections;

internal sealed class FakeMailboxOwnershipRepository : IMailboxOwnershipRepository
{
    public List<MailboxOwnership> Rows { get; } = [];

    public Task<MailboxOwnership?> GetAsync(Guid tenantId, Guid accountId, CancellationToken ct = default) =>
        Task.FromResult(Rows.FirstOrDefault(x => x.TenantId == tenantId && x.AccountId == accountId));

    public Task AddAsync(MailboxOwnership ownership, CancellationToken ct = default)
    {
        Rows.Add(ownership);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyCollection<Guid>?> ListOwnedAccountIdsAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken ct = default
    )
    {
        var ofTenant = Rows.Where(x => x.TenantId == tenantId).ToList();
        return Task.FromResult<IReadOnlyCollection<Guid>?>(
            ofTenant.Count == 0 ? null : ofTenant.Where(x => x.OwnerUserId == userId).Select(x => x.AccountId).ToList()
        );
    }
}
