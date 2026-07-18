using Microsoft.EntityFrameworkCore;
using TaxVision.Postmaster.Application.Abstractions;
using TaxVision.Postmaster.Domain.Projections;

namespace TaxVision.Postmaster.Infrastructure.Persistence.Repositories;

internal sealed class TenantOAuthAccountRepository(PostmasterDbContext db) : ITenantOAuthAccountRepository
{
    public Task<TenantOAuthAccount?> GetByAccountIdAsync(
        Guid tenantId,
        Guid accountId,
        CancellationToken ct = default
    ) => db.TenantOAuthAccounts.FirstOrDefaultAsync(a => a.TenantId == tenantId && a.AccountId == accountId, ct);

    public Task<TenantOAuthAccount?> FindActiveByTenantIdAsync(Guid tenantId, CancellationToken ct = default) =>
        db
            .TenantOAuthAccounts.Where(a => a.TenantId == tenantId && a.IsActive)
            .OrderByDescending(a => a.ConnectedAtUtc)
            .FirstOrDefaultAsync(ct);

    public async Task AddAsync(TenantOAuthAccount account, CancellationToken ct = default)
    {
        await db.TenantOAuthAccounts.AddAsync(account, ct);
    }
}
