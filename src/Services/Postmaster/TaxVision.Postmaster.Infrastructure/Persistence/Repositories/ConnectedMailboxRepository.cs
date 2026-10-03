using Microsoft.EntityFrameworkCore;
using TaxVision.Postmaster.Application.Abstractions;
using TaxVision.Postmaster.Domain.Projections;

namespace TaxVision.Postmaster.Infrastructure.Persistence.Repositories;

internal sealed class ConnectedMailboxRepository(PostmasterDbContext db) : IConnectedMailboxRepository
{
    public Task<ConnectedMailbox?> GetByAccountIdAsync(Guid tenantId, Guid accountId, CancellationToken ct = default) =>
        db.ConnectedMailboxes.FirstOrDefaultAsync(a => a.TenantId == tenantId && a.AccountId == accountId, ct);

    public Task<ConnectedMailbox?> FindActiveByTenantIdAsync(Guid tenantId, CancellationToken ct = default) =>
        db
            .ConnectedMailboxes.Where(a => a.TenantId == tenantId && a.IsActive)
            .OrderByDescending(a => a.ConnectedAtUtc)
            .FirstOrDefaultAsync(ct);

    public async Task AddAsync(ConnectedMailbox account, CancellationToken ct = default)
    {
        await db.ConnectedMailboxes.AddAsync(account, ct);
    }
}
