using Microsoft.EntityFrameworkCore;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Domain.Customers;
using DomainCustomer = TaxVision.Customer.Domain.Customers.Customer;

namespace TaxVision.Customer.Infrastructure.Persistence;

/// <summary>Conteos y top del Work Profile sobre clientes activos del tenant, con el ABAC del que mira.</summary>
public sealed class CustomerWorkReadService(CustomerDbContext db) : ICustomerWorkReadService
{
    public async Task<CustomerWorkCounts> CountAsync(Guid tenantId, Guid userId, Guid? visibleTo, CancellationToken ct)
    {
        var visible = Active(tenantId).VisibleTo(db, tenantId, visibleTo);
        var asPreparer = await visible.PreparedBy(userId).CountAsync(ct);
        var assigned = await visible.AssignedTo(db, tenantId, userId).CountAsync(ct);
        return new CustomerWorkCounts(asPreparer, assigned);
    }

    public async Task<IReadOnlyList<CustomerWorkRow>> TopAsPreparerAsync(
        Guid tenantId,
        Guid userId,
        Guid? visibleTo,
        int take,
        CancellationToken ct
    ) =>
        await Active(tenantId)
            .VisibleTo(db, tenantId, visibleTo)
            .PreparedBy(userId)
            .OrderByDescending(c => c.UpdatedAtUtc ?? c.CreatedAtUtc)
            .ThenByDescending(c => c.Id)
            .Take(take)
            .Select(c => new CustomerWorkRow(c.Id, c.DisplayName, c.Status))
            .ToListAsync(ct);

    public async Task<IReadOnlyDictionary<Guid, int>> CountAsPreparerAsync(
        Guid tenantId,
        IReadOnlyList<Guid> userIds,
        Guid? visibleTo,
        CancellationToken ct
    ) =>
        await Active(tenantId)
            .VisibleTo(db, tenantId, visibleTo)
            .Where(c => c.AssignedPreparerUserId != null && userIds.Contains(c.AssignedPreparerUserId.Value))
            .GroupBy(c => c.AssignedPreparerUserId!.Value)
            .Select(g => new { UserId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.UserId, g => g.Count, ct);

    // Activos del tenant (archivados e inactivos fuera); tenant explícito, sin filtro ambiente.
    private IQueryable<DomainCustomer> Active(Guid tenantId) =>
        db
            .Customers.AsNoTracking()
            .IgnoreQueryFilters()
            .Where(c => c.TenantId == tenantId && c.Status == CustomerStatus.Active);
}
