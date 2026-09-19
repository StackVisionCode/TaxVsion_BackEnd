using BuildingBlocks.Common;
using Microsoft.EntityFrameworkCore;
using TaxVision.Campaigns.Application.Contacts.Abstractions;
using TaxVision.Campaigns.Domain.Contacts;

namespace TaxVision.Campaigns.Infrastructure.Persistence.Repositories;

public sealed class ContactListRepository(CampaignsDbContext db) : IContactListRepository
{
    public Task<ContactList?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default) =>
        db
            .ContactLists.IgnoreQueryFilters()
            .Include(l => l.Members)
            .FirstOrDefaultAsync(l => l.Id == id && l.TenantId == tenantId, ct);

    public async Task<IReadOnlyList<Guid>> GetMemberContactIdsAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> listIds,
        CancellationToken ct = default
    )
    {
        if (listIds.Count == 0)
            return [];
        return await db
            .Set<ContactListMember>()
            .Where(m => m.TenantId == tenantId && listIds.Contains(m.ContactListId))
            .Select(m => m.ContactId)
            .Distinct()
            .ToListAsync(ct);
    }

    public async Task<PagedResult<ContactList>> ListAsync(Guid tenantId, int page, int size, CancellationToken ct = default)
    {
        var query = db.ContactLists.IgnoreQueryFilters().Where(l => l.TenantId == tenantId).OrderByDescending(l => l.CreatedAtUtc);
        var totalCount = await query.CountAsync(ct);
        var items = await query.Skip((page - 1) * size).Take(size).Include(l => l.Members).ToListAsync(ct);
        return new PagedResult<ContactList>(items, page, size, totalCount);
    }

    public async Task AddAsync(ContactList list, CancellationToken ct = default) => await db.ContactLists.AddAsync(list, ct);
}
