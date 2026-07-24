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
            .Include(c => c.FiscalProfile)
            .Where(c => c.Id == id)
            .FirstOrDefaultAsync(ct);

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
