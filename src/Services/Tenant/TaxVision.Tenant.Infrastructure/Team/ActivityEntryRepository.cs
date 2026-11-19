using BuildingBlocks.Common;
using BuildingBlocks.CustomerVisibility;
using Microsoft.EntityFrameworkCore;
using TaxVision.Tenant.Application.Team.Abstractions;
using TaxVision.Tenant.Domain.Team;
using TaxVision.Tenant.Infrastructure.Persistence;

namespace TaxVision.Tenant.Infrastructure.Team;

/// <summary>
/// IgnoreQueryFilters() + tenant explícito: los consumers corren sin tenant ambiental y la lectura llega por el bus;
/// el tenant viene del evento o del JWT, nunca de un parámetro del cliente.
/// </summary>
public sealed class ActivityEntryRepository(TenantDbContext db) : IActivityEntryRepository
{
    public Task<bool> ExistsAsync(Guid tenantId, Guid eventId, CancellationToken ct = default) =>
        db.ActivityEntries.IgnoreQueryFilters().AnyAsync(e => e.TenantId == tenantId && e.EventId == eventId, ct);

    public void Add(ActivityEntry entry) => db.ActivityEntries.Add(entry);

    public async Task<PagedResult<ActivityEntry>> ListAsync(ActivityFeedFilter filter, CancellationToken ct = default)
    {
        var query = Visible(filter);
        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(e => e.OccurredAtUtc)
            .ThenByDescending(e => e.Id)
            .Skip((filter.Page - 1) * filter.Size)
            .Take(filter.Size)
            .AsNoTracking()
            .ToListAsync(ct);
        return new PagedResult<ActivityEntry>(items, filter.Page, filter.Size, total);
    }

    private IQueryable<ActivityEntry> Visible(ActivityFeedFilter filter)
    {
        var query = db
            .ActivityEntries.IgnoreQueryFilters()
            .Where(e => e.TenantId == filter.TenantId && e.ActorUserId == filter.MemberUserId);
        if (filter.FromUtc is { } from)
            query = query.Where(e => e.OccurredAtUtc >= from);
        if (filter.ToUtc is { } to)
            query = query.Where(e => e.OccurredAtUtc < to);
        if (filter.Verbs.Count > 0)
            query = query.Where(e => filter.Verbs.Contains(e.Verb));
        if (!filter.IncludePrivate)
            query = query.Where(e => e.Visibility != ActivityVisibility.Private);
        if (!filter.IncludeRestricted)
            query = query.Where(e => e.Visibility != ActivityVisibility.Restricted);
        return filter.AssignedCustomersOf is { } viewer ? OnlyAssignedCustomers(query, filter.TenantId, viewer) : query;
    }

    // ABAC: sin cliente, o de un cliente asignado a quien mira (proyección compartida de Customer).
    private IQueryable<ActivityEntry> OnlyAssignedCustomers(
        IQueryable<ActivityEntry> query,
        Guid tenantId,
        Guid viewer
    ) =>
        query.Where(e =>
            e.CustomerId == null
            || db.Set<CustomerAssignmentProjection>()
                .Any(a => a.TenantId == tenantId && a.UserId == viewer && a.CustomerId == e.CustomerId)
        );
}
