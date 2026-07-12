using Microsoft.EntityFrameworkCore;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Audit;

namespace TaxVision.Signature.Infrastructure.Persistence.Repositories;

public sealed class SignatureAuditRepository(SignatureDbContext db) : ISignatureAuditRepository
{
    public async Task<AuditChainTail?> GetTailAsync(
        Guid tenantId,
        Guid signatureRequestId,
        CancellationToken ct = default
    )
    {
        var tail = await db
            .SignatureAuditEvents.AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.SignatureRequestId == signatureRequestId)
            .OrderByDescending(e => e.Sequence)
            .Select(e => new { e.Sequence, e.ChainHash })
            .FirstOrDefaultAsync(ct);
        return tail is null ? null : new AuditChainTail(tail.Sequence, tail.ChainHash);
    }

    public async Task AddAsync(SignatureAuditEvent evt, CancellationToken ct = default) =>
        await db.SignatureAuditEvents.AddAsync(evt, ct);

    public async Task<IReadOnlyList<SignatureAuditEvent>> ListAsync(
        Guid tenantId,
        Guid signatureRequestId,
        CancellationToken ct = default
    ) =>
        await db
            .SignatureAuditEvents.AsNoTracking()
            .Where(e => e.TenantId == tenantId && e.SignatureRequestId == signatureRequestId)
            .OrderBy(e => e.Sequence)
            .ToListAsync(ct);
}
