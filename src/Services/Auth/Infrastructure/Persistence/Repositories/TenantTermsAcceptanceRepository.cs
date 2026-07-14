using Microsoft.EntityFrameworkCore;
using TaxVision.Auth.Application.Abstractions;
using TaxVision.Auth.Domain.Terms;

namespace TaxVision.Auth.Infrastructure.Persistence.Repositories;

public sealed class TenantTermsAcceptanceRepository(AuthDbContext db) : ITenantTermsAcceptanceRepository
{
    public async Task AddAsync(TenantTermsAcceptance acceptance, CancellationToken ct = default) =>
        await db.TenantTermsAcceptances.AddAsync(acceptance, ct);

    public Task<TenantTermsAcceptance?> GetLatestAsync(Guid tenantId, CancellationToken ct = default) =>
        db
            .TenantTermsAcceptances.AsNoTracking()
            .Where(acceptance => acceptance.TenantId == tenantId)
            .OrderByDescending(acceptance => acceptance.AcceptedAtUtc)
            .FirstOrDefaultAsync(ct);
}
