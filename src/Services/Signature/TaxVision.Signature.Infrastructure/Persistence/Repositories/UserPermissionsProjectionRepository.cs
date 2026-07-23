using Microsoft.EntityFrameworkCore;
using TaxVision.Signature.Application.Abstractions;
using TaxVision.Signature.Domain.Projections;

namespace TaxVision.Signature.Infrastructure.Persistence.Repositories;

public sealed class UserPermissionsProjectionRepository(SignatureDbContext db) : IUserPermissionsProjectionRepository
{
    // Bug real de producción (2026-07-22, mismo patrón que FileObjectRepository.GetAsync en
    // CloudStorage): este consumer Wolverine corre sin TenantContext ambiente (no hay HTTP
    // request), así que el filtro global de tenant de SignatureDbContext tira antes de llegar
    // acá. tenantId ya viene explícito y confiable desde el evento — IgnoreQueryFilters() explícito.
    public Task<UserPermissionsProjection?> GetAsync(Guid tenantId, Guid userId, CancellationToken ct = default) =>
        db
            .UserPermissionsProjections.IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.UserId == userId, ct);

    public async Task AddAsync(UserPermissionsProjection projection, CancellationToken ct = default) =>
        await db.UserPermissionsProjections.AddAsync(projection, ct);
}
