using Microsoft.EntityFrameworkCore;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Projections;

namespace TaxVision.Signature.Infrastructure.Persistence.Repositories;

public sealed class SignerRoleAuditSnapshotRepository(SignatureDbContext db) : ISignerRoleAuditSnapshotRepository
{
    // Consumer Wolverine sin TenantContext ambiente (no hay HTTP request) — el filtro global de
    // tenant de SignatureDbContext tiraría antes de llegar acá. tenantId ya viene explícito y
    // confiable desde el evento — IgnoreQueryFilters() explícito.
    public Task<SignerRoleAuditSnapshot?> GetAsync(Guid tenantId, Guid userId, CancellationToken ct = default) =>
        db
            .SignerRoleAuditSnapshots.IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.UserId == userId, ct);

    public async Task AddAsync(SignerRoleAuditSnapshot projection, CancellationToken ct = default) =>
        await db.SignerRoleAuditSnapshots.AddAsync(projection, ct);
}
