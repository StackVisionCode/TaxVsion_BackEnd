using TaxVision.Postmaster.Application.Abstractions;
using TaxVision.Postmaster.Domain.Projections;

namespace TaxVision.Postmaster.Tests.Providers;

internal sealed class FakeConnectedMailboxRepository : IConnectedMailboxRepository
{
    private readonly List<ConnectedMailbox> _accounts = [];

    public Task AddAsync(ConnectedMailbox account, CancellationToken ct = default)
    {
        _accounts.Add(account);
        return Task.CompletedTask;
    }

    public Task<ConnectedMailbox?> GetByAccountIdAsync(Guid tenantId, Guid accountId, CancellationToken ct = default) =>
        Task.FromResult(_accounts.Find(a => a.TenantId == tenantId && a.AccountId == accountId));

    public Task<ConnectedMailbox?> FindActiveByTenantIdAsync(Guid tenantId, CancellationToken ct = default) =>
        Task.FromResult(
            _accounts
                .Where(a => a.TenantId == tenantId && a.IsActive)
                .OrderByDescending(a => a.ConnectedAtUtc)
                .FirstOrDefault()
        );
}
