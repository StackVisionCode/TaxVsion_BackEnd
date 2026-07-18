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
        db
            .ConsentEvents.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.SignatureRequestId == signatureRequestId && c.SignerId == signerId)
            .OrderByDescending(c => c.AcceptedAtUtc)
            .FirstOrDefaultAsync(ct);
}
