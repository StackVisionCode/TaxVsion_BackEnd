using Microsoft.EntityFrameworkCore;
using TaxVision.Customer.Application.Customers;
using DomainCustomer = TaxVision.Customer.Domain.Customers.Customer;

namespace TaxVision.Customer.Infrastructure.Persistence;

/// <summary>
/// Filtros de clientes por persona del staff. IgnoreQueryFilters + tenant explícito en el subquery: corren en el
/// scope de Wolverine, sin tenant ambiente.
/// </summary>
internal static class CustomerStaffQueries
{
    public static IQueryable<DomainCustomer> PreparedBy(this IQueryable<DomainCustomer> query, Guid userId) =>
        query.Where(c => c.AssignedPreparerUserId == userId);

    // Asignado = primary (campo denormalizado, cubre datos previos al backfill) o con fila de asignación.
    public static IQueryable<DomainCustomer> AssignedTo(
        this IQueryable<DomainCustomer> query,
        CustomerDbContext db,
        Guid tenantId,
        Guid userId
    ) =>
        query.Where(c =>
            c.AssignedPreparerUserId == userId
            || db.CustomerAssignments.IgnoreQueryFilters()
                .Any(a => a.TenantId == tenantId && a.CustomerId == c.Id && a.UserId == userId)
        );

    // ABAC del listado: solo los clientes con fila de asignación del que mira. null = sin restricción.
    public static IQueryable<DomainCustomer> VisibleTo(
        this IQueryable<DomainCustomer> query,
        CustomerDbContext db,
        Guid tenantId,
        Guid? viewerUserId
    ) =>
        viewerUserId is { } viewer
            ? query.Where(c =>
                db.CustomerAssignments.IgnoreQueryFilters()
                    .Any(a => a.TenantId == tenantId && a.CustomerId == c.Id && a.UserId == viewer)
            )
            : query;

    public static IQueryable<DomainCustomer> ForStaff(
        this IQueryable<DomainCustomer> query,
        CustomerDbContext db,
        Guid tenantId,
        CustomerStaffFilter? staff
    )
    {
        if (staff?.PreparerUserId is { } preparer)
            query = query.PreparedBy(preparer);
        if (staff?.AssignedUserId is { } assigned)
            query = query.AssignedTo(db, tenantId, assigned);
        return query;
    }
}
