using BuildingBlocks.Permissions;
using Microsoft.EntityFrameworkCore;
using TaxVision.Reminder.Application.Permissions.Abstractions;
using TaxVision.Reminder.Domain.Permissions;

namespace TaxVision.Reminder.Infrastructure.Persistence.Repositories;

// Implementa dos interfaces sobre la misma tabla: el puerto local rico (usado por los consumers
// para escribir/leer la proyección) y el puerto angosto de BuildingBlocks
// (IUserPermissionsProjectionReader.GetSnapshotAsync, el único método que necesita
// ProjectionPermissionsSource para autorizar) — una sola instancia scoped resuelve ambas.
public sealed class UserPermissionsProjectionRepository(ReminderDbContext db)
    : IUserPermissionsProjectionRepository,
        IUserPermissionsProjectionReader
{
    // Consumer Wolverine sin TenantContext ambiente (no hay HTTP request) — el filtro global de
    // tenant tiraría antes de llegar acá. tenantId ya viene explícito y confiable desde el evento.
    public async Task<UserPermissionsProjection?> GetAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken ct = default
    ) =>
        await db
            .UserPermissionsProjections.IgnoreQueryFilters()
            .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.UserId == userId, ct);

    public async Task AddAsync(UserPermissionsProjection projection, CancellationToken ct = default) =>
        await db.UserPermissionsProjections.AddAsync(projection, ct);

    public async Task<IReadOnlyList<UserPermissionsProjection>> FindActiveByTenantAndRoleIdAsync(
        Guid tenantId,
        Guid roleId,
        CancellationToken ct = default
    )
    {
        // RoleIds vive serializado en JSON, así que el filtro por rol no es traducible a SQL: se
        // trae a los activos del tenant y se filtra en memoria. Mismo criterio que Notes.
        var candidates = await db
            .UserPermissionsProjections.IgnoreQueryFilters()
            .Where(p => p.TenantId == tenantId && p.IsActive)
            .ToListAsync(ct);

        return candidates.Where(p => p.RoleIds().Contains(roleId)).ToList();
    }

    // A diferencia de los métodos de consumer, este corre DENTRO del request HTTP con el tenant ya
    // poblado por JwtTenantContextMiddleware — el filtro global aplica y es deseable que aplique.
    public async Task<UserPermissionsSnapshot?> GetSnapshotAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken ct = default
    )
    {
        var projection = await db
            .UserPermissionsProjections.AsNoTracking()
            .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.UserId == userId && p.IsActive, ct);

        return projection is null
            ? null
            : new UserPermissionsSnapshot(projection.PermissionsVersion, projection.PermissionCodes());
    }
}
