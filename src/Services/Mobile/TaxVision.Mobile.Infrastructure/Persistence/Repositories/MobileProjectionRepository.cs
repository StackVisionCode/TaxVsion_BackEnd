using BuildingBlocks.Permissions;
using Microsoft.EntityFrameworkCore;
using TaxVision.Mobile.Application.Projections.Abstractions;
using TaxVision.Mobile.Domain.Projections;

namespace TaxVision.Mobile.Infrastructure.Persistence.Repositories;

public sealed class MobileProjectionRepository(MobileDbContext db)
    : IMobileProjectionRepository,
        IUserPermissionsProjectionReader,
        IUserPermissionsProjectionWriter
{
    public Task<UserPermissionsProjection?> FindUserPermissionsAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken ct = default
    ) =>
        // Sin filtro global: la usan consumers y el pull de permisos, con la oficina explícita.
        db
            .UserPermissionsProjections.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.UserId == userId, ct);

    public void AddUserPermissions(UserPermissionsProjection row) => db.UserPermissionsProjections.Add(row);

    public Task<RolePermissionsProjection?> FindRolePermissionsAsync(
        Guid tenantId,
        Guid roleId,
        CancellationToken ct = default
    ) =>
        // Sin filtro global: la usan consumers, con la oficina explícita.
        db
            .RolePermissionsProjections.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Id == roleId, ct);

    public void AddRolePermissions(RolePermissionsProjection row) => db.RolePermissionsProjections.Add(row);

    public async Task<UserPermissionsSnapshot?> GetSnapshotAsync(
        Guid tenantId,
        Guid userId,
        CancellationToken ct = default
    )
    {
        // Sin filtro global: corre antes de fijar la oficina del request; la oficina va explícita.
        var row = await db
            .UserPermissionsProjections.IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(
                x => x.TenantId == tenantId && x.UserId == userId && x.IsActive && x.HasPermissionsSnapshot,
                ct
            );
        return row is null ? null : new UserPermissionsSnapshot(row.PermissionsVersion, row.PermissionCodes());
    }

    public async Task PersistSnapshotAsync(
        Guid tenantId,
        Guid userId,
        RemotePermissionsSnapshot snapshot,
        CancellationToken ct = default
    )
    {
        var row = await FindUserPermissionsAsync(tenantId, userId, ct);
        if (row is null)
            AddUserPermissions(
                UserPermissionsProjection.Create(
                    tenantId,
                    userId,
                    snapshot.PermissionsVersion,
                    snapshot.PermissionCodes,
                    snapshot.RoleIds,
                    DateTime.UtcNow
                )
            );
        else
            row.ApplyIfNewer(snapshot.PermissionsVersion, snapshot.PermissionCodes, snapshot.RoleIds, DateTime.UtcNow);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (BuildingBlocks.Results.ConflictException ex) when (ex.Code == "Persistence.UniqueConstraint")
        {
            db.ChangeTracker.Clear();
        }
    }
}
