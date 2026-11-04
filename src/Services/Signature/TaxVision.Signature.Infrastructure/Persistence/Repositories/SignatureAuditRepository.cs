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
        var saved = await db
            .SignatureAuditEvents.AsNoTracking()
            .IgnoreQueryFilters()
            .Where(e => e.TenantId == tenantId && e.SignatureRequestId == signatureRequestId)
            .OrderByDescending(e => e.Sequence)
            .Select(e => new AuditChainTail(e.Sequence, e.ChainHash))
            .FirstOrDefaultAsync(ct);

        // Eventos agregados en esta misma unidad de trabajo y aún sin guardar: dos appends seguidos
        // se encadenan en vez de leer los dos el mismo tail de la base (misma secuencia → índice único).
        var pending = db
            .SignatureAuditEvents.Local.Where(e => e.TenantId == tenantId && e.SignatureRequestId == signatureRequestId)
            .OrderByDescending(e => e.Sequence)
            .Select(e => new AuditChainTail(e.Sequence, e.ChainHash))
            .FirstOrDefault();

        return pending is not null && (saved is null || pending.LastSequence > saved.LastSequence) ? pending : saved;
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
