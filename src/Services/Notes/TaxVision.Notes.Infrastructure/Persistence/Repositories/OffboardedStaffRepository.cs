using Microsoft.EntityFrameworkCore;
using TaxVision.Notes.Application.Projections.Abstractions;
using TaxVision.Notes.Domain.Projections;

namespace TaxVision.Notes.Infrastructure.Persistence.Repositories;

// Se ESCRIBE desde el consumer (scope Wolverine sin TenantContext ambiente → el filtro global tiraría)
// y se LEE desde el hot path de autorización (scope HTTP). IgnoreQueryFilters + tenant explícito
// funciona en ambos; tenantId siempre viene confiable (del evento, o del recurso resuelto con el JWT).
public sealed class OffboardedStaffRepository(NotesDbContext db) : IOffboardedStaffRepository
{
    public Task<bool> IsOffboardedAsync(Guid tenantId, Guid userId, CancellationToken ct = default) =>
        db.OffboardedStaff.IgnoreQueryFilters().AnyAsync(p => p.TenantId == tenantId && p.UserId == userId, ct);

    public async Task<OffboardedStaffProjection?> GetAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken ct = default
    ) =>
        await db
            .OffboardedStaff.IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.UserId == userId, ct);

    public async Task AddAsync(OffboardedStaffProjection projection, CancellationToken ct = default) =>
        await db.OffboardedStaff.AddAsync(projection, ct);
}
