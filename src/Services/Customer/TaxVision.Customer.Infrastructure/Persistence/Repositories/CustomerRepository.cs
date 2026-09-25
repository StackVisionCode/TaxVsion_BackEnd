using Microsoft.EntityFrameworkCore;
using TaxVision.Customer.Application.Abstractions;
using DomainCustomer = TaxVision.Customer.Domain.Customers.Customer;

namespace TaxVision.Customer.Infrastructure.Persistence.Repositories;

public sealed class CustomerRepository(CustomerDbContext db) : ICustomerRepository
{
    // IgnoreQueryFilters(): mismo bug que UserRepository.GetByIdAsync (Auth) — corre en handlers
    // de Wolverine (bus.InvokeAsync) donde el ITenantContext ambiente puede llegar vacío al scope
    // de DI del handler, y este método toma solo Id puro (sin tenantId explícito). Es seguro:
    // todos los ~24 llamadores validan post-fetch (customer.TenantId != cmd.TenantId) —
    // Update/Deactivate/Archive/AssignPreparer/AddAddress/AddRelation/etc.
    public Task<DomainCustomer?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        db
            .Customers.IgnoreQueryFilters()
            .Include(c => c.Addresses)
            .Include(c => c.ContactPoints)
            .Include(c => c.Relations)
            .Include(c => c.Assignments)
            .Include(c => c.FiscalProfile)
            .Where(c => c.Id == id)
            .FirstOrDefaultAsync(ct);

    // Tracked (sin AsNoTracking): el consumer de offboard reasigna el preparador y persiste. Include(Assignments):
    // el handover/unassign mutan la colección de asignaciones — sin cargarla operarían sobre una vacía y
    // dejarían filas huérfanas. Excluye archivados (no se trabajan).
    public async Task<IReadOnlyList<DomainCustomer>> ListByAssignedPreparerAsync(
        Guid tenantId,
        Guid preparerUserId,
        int batchSize,
        Guid afterId,
        CancellationToken ct
    ) =>
        await db
            .Customers.IgnoreQueryFilters()
            .Include(c => c.Assignments)
            .Where(c =>
                c.TenantId == tenantId
                && c.AssignedPreparerUserId == preparerUserId
                && c.Status != TaxVision.Customer.Domain.Customers.CustomerStatus.Archived
                && c.Id > afterId
            )
            .OrderBy(c => c.Id)
            .Take(batchSize)
            .ToListAsync(ct);

    // Clientes activos donde el usuario tiene un acceso ADICIONAL (fila no-primary) — para revocárselos al
    // retirarlo. Tracked + Include(Assignments) (RevokeAccess muta la colección). Keyset por Id.
    public async Task<IReadOnlyList<DomainCustomer>> ListNonPrimaryAssignedAsync(
        Guid tenantId,
        Guid userId,
        int batchSize,
        Guid afterId,
        CancellationToken ct
    ) =>
        await db
            .Customers.IgnoreQueryFilters()
            .Include(c => c.Assignments)
            .Where(c =>
                c.TenantId == tenantId
                && c.Status != TaxVision.Customer.Domain.Customers.CustomerStatus.Archived
                && c.Id > afterId
                && db.CustomerAssignments.IgnoreQueryFilters()
                    .Any(a => a.TenantId == tenantId && a.CustomerId == c.Id && a.UserId == userId && !a.IsPrimary)
            )
            .OrderBy(c => c.Id)
            .Take(batchSize)
            .ToListAsync(ct);

    // Para el pre-flight de impacto al retirar al empleado: cuenta clientes activos donde es primary
    // (campo denormalizado, sirve para datos previos al backfill) O tiene una fila de asignación. Sin
    // doble conteo (WHERE por cliente). IgnoreQueryFilters + tenant explícito: sirve desde HTTP o Wolverine.
    public Task<int> CountActiveByAssignedPreparerAsync(Guid tenantId, Guid preparerUserId, CancellationToken ct) =>
        db
            .Customers.IgnoreQueryFilters()
            .CountAsync(
                c =>
                    c.TenantId == tenantId
                    && c.Status != TaxVision.Customer.Domain.Customers.CustomerStatus.Archived
                    && (
                        c.AssignedPreparerUserId == preparerUserId
                        || db.CustomerAssignments.IgnoreQueryFilters()
                            .Any(a => a.TenantId == tenantId && a.CustomerId == c.Id && a.UserId == preparerUserId)
                    ),
                ct
            );

    public async Task<IReadOnlyList<DomainCustomer>> GetByIdsAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> ids,
        CancellationToken ct
    )
    {
        if (ids.Count == 0)
            return [];

        return await db
            .Customers.IgnoreQueryFilters()
            .Where(c => c.TenantId == tenantId && ids.Contains(c.Id))
            .ToListAsync(ct);
    }

    // Reparto masivo (bulk assign): carga por lote los clientes asignables con sus asignaciones, para
    // mutarlos con el agregado (AssignPreparer/GrantAccess) y guardar de una. IgnoreQueryFilters + tenant
    // explícito (scope de Wolverine sin tenant ambiente). Excluye archivados. 1 query, no N round-trips.
    public async Task<IReadOnlyList<DomainCustomer>> ListForAssignmentAsync(
        Guid tenantId,
        IReadOnlyCollection<Guid> ids,
        CancellationToken ct
    )
    {
        if (ids.Count == 0)
            return [];

        return await db
            .Customers.IgnoreQueryFilters()
            .Include(c => c.Assignments)
            .Where(c =>
                c.TenantId == tenantId
                && ids.Contains(c.Id)
                && c.Status != TaxVision.Customer.Domain.Customers.CustomerStatus.Archived
            )
            .ToListAsync(ct);
    }

    public async Task<Guid?> FindCustomerIdByFiscalBlindIndexAsync(
        Guid tenantId,
        string blindIndex,
        Guid? excludeCustomerId,
        CancellationToken ct
    )
    {
        var query = db
            .CustomerFiscalProfiles.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(fp => fp.TenantId == tenantId && fp.TaxIdentifierBlindIndex == blindIndex);

        if (excludeCustomerId.HasValue)
            query = query.Where(fp => fp.CustomerId != excludeCustomerId.Value);

        return await query.Select(fp => (Guid?)fp.CustomerId).FirstOrDefaultAsync(ct);
    }

    public async Task<Guid?> FindRelationIdByFiscalBlindIndexAsync(
        Guid tenantId,
        string blindIndex,
        Guid? excludeRelationId,
        CancellationToken ct
    )
    {
        var query = db
            .CustomerRelationFiscalProfiles.IgnoreQueryFilters()
            .AsNoTracking()
            .Where(fp => fp.TenantId == tenantId && fp.TaxIdentifierBlindIndex == blindIndex);

        if (excludeRelationId.HasValue)
            query = query.Where(fp => fp.CustomerRelationId != excludeRelationId.Value);

        return await query.Select(fp => (Guid?)fp.CustomerRelationId).FirstOrDefaultAsync(ct);
    }

    public async Task AddAsync(DomainCustomer customer, CancellationToken ct = default)
    {
        await db.Customers.AddAsync(customer, ct);
    }
}
