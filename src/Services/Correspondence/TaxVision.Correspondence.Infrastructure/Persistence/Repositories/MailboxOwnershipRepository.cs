using Microsoft.EntityFrameworkCore;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Domain.Projections;

namespace TaxVision.Correspondence.Infrastructure.Persistence.Repositories;

internal sealed class MailboxOwnershipRepository(CorrespondenceDbContext db) : IMailboxOwnershipRepository
{
    public Task<MailboxOwnership?> GetAsync(Guid tenantId, Guid accountId, CancellationToken ct = default) =>
        db
            .MailboxOwnerships.IgnoreQueryFilters()
            .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.AccountId == accountId, ct);

    public async Task AddAsync(MailboxOwnership ownership, CancellationToken ct = default) =>
        await db.MailboxOwnerships.AddAsync(ownership, ct);

    public async Task<IReadOnlyCollection<Guid>?> ListOwnedAccountIdsAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken ct = default
    )
    {
        var rows = db.MailboxOwnerships.IgnoreQueryFilters().Where(x => x.TenantId == tenantId);
        if (!await rows.AnyAsync(ct))
            return null;

        return await rows.Where(x => x.OwnerUserId == userId).Select(x => x.AccountId).ToListAsync(ct);
    }
}
