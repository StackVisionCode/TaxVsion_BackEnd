using BuildingBlocks.Common;
using Microsoft.EntityFrameworkCore;
using TaxVision.Campaigns.Application.Contacts.Abstractions;
using TaxVision.Campaigns.Domain.Contacts;

namespace TaxVision.Campaigns.Infrastructure.Persistence.Repositories;

public sealed class ContactRepository(CampaignsDbContext db) : IContactRepository
{
    // IgnoreQueryFilters(): tenantId ya viene explícito y validado desde Application (mismo criterio
    // que los demás repos — el filtro ambiental global no está garantizado poblado en el scope de DI
    // de un handler de Wolverine).
    public Task<Contact?> GetByIdAsync(Guid tenantId, Guid id, CancellationToken ct = default) =>
        db.Contacts.IgnoreQueryFilters().FirstOrDefaultAsync(c => c.Id == id && c.TenantId == tenantId, ct);

    public Task<Contact?> FindByDestinationAsync(
        Guid tenantId,
        string? email,
        string? phoneE164,
        CancellationToken ct = default
    )
    {
        var e = string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();
        var p = string.IsNullOrWhiteSpace(phoneE164) ? null : phoneE164.Trim();
        if (e is null && p is null)
            return Task.FromResult<Contact?>(null);

        return db
            .Contacts.IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                c => c.TenantId == tenantId && ((e != null && c.Email == e) || (p != null && c.PhoneE164 == p)),
                ct
            );
    }

    public async Task<IReadOnlyList<Contact>> GetManyByIdsAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> ids,
        CancellationToken ct = default
    )
    {
        if (ids.Count == 0)
            return [];
        return await db
            .Contacts.IgnoreQueryFilters()
            .Where(c => c.TenantId == tenantId && ids.Contains(c.Id))
            .ToListAsync(ct);
    }

    public async Task<PagedResult<Contact>> ListAsync(Guid tenantId, int page, int size, CancellationToken ct = default)
    {
        var query = db.Contacts.IgnoreQueryFilters().Where(c => c.TenantId == tenantId).OrderByDescending(c => c.CreatedAtUtc);
        var totalCount = await query.CountAsync(ct);
        var items = await query.Skip((page - 1) * size).Take(size).ToListAsync(ct);
        return new PagedResult<Contact>(items, page, size, totalCount);
    }

    public async Task AddAsync(Contact contact, CancellationToken ct = default) => await db.Contacts.AddAsync(contact, ct);
}
