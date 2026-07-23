using Microsoft.EntityFrameworkCore;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Consents;

namespace TaxVision.Signature.Infrastructure.Persistence.Repositories;

public sealed class ConsentEventRepository(SignatureDbContext db) : IConsentEventRepository
{
    public async Task AddAsync(ConsentEvent evt, CancellationToken ct = default) =>
        await db.ConsentEvents.AddAsync(evt, ct);

    public Task<ConsentEvent?> GetLatestForSignerAsync(
        Guid tenantId,
        Guid signatureRequestId,
        Guid signerId,
        CancellationToken ct = default
    ) =>
        // Mismo bug de scope de Wolverine (ver LocalCommandTenantMiddleware.cs): tenantId ya viene
        // explícito y validado — IgnoreQueryFilters() porque el filtro ambiental global puede no
        // estar poblado en este scope de DI.
        db
            .ConsentEvents.AsNoTracking()
            .IgnoreQueryFilters()
            .Where(c => c.TenantId == tenantId && c.SignatureRequestId == signatureRequestId && c.SignerId == signerId)
            .OrderByDescending(c => c.AcceptedAtUtc)
            .FirstOrDefaultAsync(ct);
}
