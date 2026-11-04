using Microsoft.EntityFrameworkCore;
using TaxVision.Billing.Application.Abstractions;
using TaxVision.Billing.Domain.Invoices;

namespace TaxVision.Billing.Infrastructure.Persistence.Repositories;

public sealed class IssuerProfileRepository(BillingDbContext dbContext) : IIssuerProfileRepository
{
    // IgnoreQueryFilters + tenant explícito: alcanzable desde CreateInvoiceDraft (bus.InvokeAsync) y
    // desde el consumer de TenantProfileChanged (scope de Wolverine sin tenant ambiental).
    public Task<IssuerProfile?> GetByTenantAsync(Guid tenantId, CancellationToken ct = default) =>
        dbContext.IssuerProfiles.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.TenantId == tenantId, ct);

    public async Task AddAsync(IssuerProfile profile, CancellationToken ct = default) =>
        await dbContext.IssuerProfiles.AddAsync(profile, ct);

    // Cross-tenant a propósito (listado M2M de la PlatformTenant): sin filtro global, orden estable.
    public async Task<(IReadOnlyList<IssuerProfile> Items, int TotalCount)> ListPageAsync(
        int page,
        int size,
        CancellationToken ct = default
    )
    {
        var query = dbContext.IssuerProfiles.IgnoreQueryFilters().AsNoTracking();
        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(p => p.TenantId).Skip((page - 1) * size).Take(size).ToListAsync(ct);
        return (items, total);
    }
}
