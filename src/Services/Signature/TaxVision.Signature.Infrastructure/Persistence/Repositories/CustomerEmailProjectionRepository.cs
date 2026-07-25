using Microsoft.EntityFrameworkCore;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Projections;

namespace TaxVision.Signature.Infrastructure.Persistence.Repositories;

internal sealed class CustomerEmailProjectionRepository(SignatureDbContext db) : ICustomerEmailProjectionRepository
{
    // Mismo bug de scope de Wolverine (ver LocalCommandTenantMiddleware.cs): tenantId ya viene
    // explícito y validado — IgnoreQueryFilters() porque el filtro ambiental global puede no
    // estar poblado en este scope de DI.
    public Task<CustomerEmailProjection?> GetByCustomerIdAsync(
        Guid tenantId,
        Guid customerId,
        CancellationToken ct = default
    ) =>
        db
            .CustomerEmailProjections.IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.CustomerId == customerId, ct);

    public Task<CustomerEmailProjection?> FindActiveByEmailAsync(
        Guid tenantId,
        string normalizedEmail,
        CancellationToken ct = default
    ) =>
        db
            .CustomerEmailProjections.IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                p => p.TenantId == tenantId && p.NormalizedEmail == normalizedEmail && !p.IsArchived,
                ct
            );

    public async Task AddAsync(CustomerEmailProjection projection, CancellationToken ct = default)
    {
        await db.CustomerEmailProjections.AddAsync(projection, ct);
    }
}
