using Microsoft.EntityFrameworkCore;
using TaxVision.Customer.Application.Abstractions;
using TaxVision.Customer.Domain.Imports;
using TaxVision.Customer.Infrastructure.Persistence;

namespace TaxVision.Customer.Infrastructure.Imports;

// Cruza oficinas a propósito (barrido de plataforma): IgnoreQueryFilters explícito.
internal sealed class CustomerImportRecoveryStore(CustomerDbContext db) : ICustomerImportRecoveryStore
{
    public async Task<IReadOnlyList<Guid>> ListStuckIdsAsync(DateTime idleSinceUtc, int max, CancellationToken ct) =>
        await db
            .CustomerImportAttempts.AsNoTracking()
            .IgnoreQueryFilters()
            .Where(a =>
                (
                    a.Status == ImportStatus.Validating
                    || a.Status == ImportStatus.Applying
                    || a.Status == ImportStatus.Canceling
                )
                && (a.LastProgressAtUtc ?? a.StartedAtUtc ?? a.CreatedAtUtc) < idleSinceUtc
            )
            .OrderBy(a => a.CreatedAtUtc)
            .Select(a => a.Id)
            .Take(max)
            .ToListAsync(ct);
}
