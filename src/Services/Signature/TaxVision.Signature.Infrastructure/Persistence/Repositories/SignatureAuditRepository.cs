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
        // Mismo bug de scope de Wolverine (ver LocalCommandTenantMiddleware.cs): tenantId ya viene
        // explícito y validado — IgnoreQueryFilters() porque el filtro ambiental global puede no
        // estar poblado en este scope de DI.
        var tail = await db
            .SignatureAuditEvents.AsNoTracking()
            .IgnoreQueryFilters()
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
        // Mismo bug de scope de Wolverine (ver LocalCommandTenantMiddleware.cs): tenantId ya viene
        // explícito y validado — IgnoreQueryFilters() porque el filtro ambiental global puede no
        // estar poblado en este scope de DI.
        await db
            .SignatureAuditEvents.AsNoTracking()
            .IgnoreQueryFilters()
            .Where(e => e.TenantId == tenantId && e.SignatureRequestId == signatureRequestId)
            .OrderBy(e => e.Sequence)
            .ToListAsync(ct);
}
