using BuildingBlocks.Results;
using TaxVision.Auth.Application.Abstractions;

namespace TaxVision.Auth.Application.Permissions.Internal.Queries;

public sealed record GetPermissionsSnapshotQuery(Guid TenantId, Guid UserId);

public sealed record PermissionsSnapshotResponse(
    int PermissionsVersion,
    IReadOnlyList<string> PermissionCodes,
    IReadOnlyList<Guid> RoleIds
);

/// <summary>
/// Opción B (recuperación pull bajo demanda de proyecciones de permisos) — M2M-only, invocado por
/// <c>ProjectionPermissionsSource</c> de un microservicio consumidor (vía su propio
/// <c>IPermissionsSnapshotClient</c>) cuando encuentra un miss local. Reusa exactamente la misma
/// lectura que <see cref="TaxVision.Auth.Api.Bootstrap.PermissionsBackfillService"/> ya hace por
/// cada usuario del backfill global (<c>GetUserRolesAsync</c> + <c>GetEffectivePermissionCodesAsync</c>)
/// — este endpoint es la contraparte "un usuario, ahora mismo" de ese job "todos los usuarios, una
/// vez al arrancar". A diferencia de ese job (background service, sin HTTP, con acceso directo a
/// <c>TenantContext</c>), este handler es Application layer (no puede depender de
/// <c>BuildingBlocks.Web</c>) y corre vía <c>bus.InvokeAsync</c> desde un controller — mismo scope
/// de DI que la request HTTP, pero <c>ITenantContext</c> no es confiable ahí (ver comentario de
/// <c>UserRepository.GetByIdAsync</c>), así que este endpoint no lo usa en absoluto:
/// <see cref="IRoleRepository.GetUserRolesAsync"/>/<see cref="IRoleRepository.GetEffectivePermissionCodesAsync"/>
/// ahora usan <c>IgnoreQueryFilters()</c> (fix aplicado junto con esta Opción B, ver su comentario)
/// porque <c>userId</c> ya es el límite real de autorización.
/// </summary>
public static class GetPermissionsSnapshotHandler
{
    public static async Task<Result<PermissionsSnapshotResponse>> Handle(
        GetPermissionsSnapshotQuery query,
        IUserRepository users,
        IRoleRepository roles,
        CancellationToken ct
    )
    {
        var user = await users.GetByIdAsync(query.UserId, ct);
        if (user is null || user.TenantId != query.TenantId)
            return Result.Failure<PermissionsSnapshotResponse>(
                new Error("Auth.UserNotFound", "The user does not exist in the given tenant.")
            );

        if (!user.IsActive)
            return Result.Failure<PermissionsSnapshotResponse>(
                new Error("Auth.UserInactive", "The user is not active.")
            );

        var userRoles = await roles.GetUserRolesAsync(user.Id, ct);
        var permissionCodes = await roles.GetEffectivePermissionCodesAsync(user.Id, ct);

        return Result.Success(
            new PermissionsSnapshotResponse(
                user.PermissionsVersion,
                permissionCodes.ToArray(),
                userRoles.Select(role => role.Id).ToArray()
            )
        );
    }
}
