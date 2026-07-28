using Microsoft.EntityFrameworkCore;
using TaxVision.Billing.Application.Abstractions;
using TaxVision.Billing.Domain.Invoices;

namespace TaxVision.Billing.Infrastructure.Persistence.Repositories;

public sealed class IssuerProfileRepository(BillingDbContext dbContext) : IIssuerProfileRepository
{
    // IgnoreQueryFilters + tenant explícito: alcanzable desde CreateInvoiceDraft (bus.InvokeAsync).
    public Task<IssuerProfile?> GetByTenantAsync(Guid tenantId, CancellationToken ct = default) =>
        dbContext.IssuerProfiles.IgnoreQueryFilters().FirstOrDefaultAsync(p => p.TenantId == tenantId, ct);

    public async Task AddAsync(IssuerProfile profile, CancellationToken ct = default) =>
        await dbContext.IssuerProfiles.AddAsync(profile, ct);
}
