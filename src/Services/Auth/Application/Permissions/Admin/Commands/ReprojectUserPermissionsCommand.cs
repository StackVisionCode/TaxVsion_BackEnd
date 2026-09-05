using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using Microsoft.Extensions.Logging;
using TaxVision.Auth.Application.Abstractions;
using Wolverine;

namespace TaxVision.Auth.Application.Permissions.Admin.Commands;

public sealed record ReprojectUserPermissionsCommand(Guid UserId);

public sealed record ReprojectUserPermissionsResult(
    Guid UserId,
    Guid TenantId,
    int PermissionsVersion,
    int PermissionCodeCount,
    int RoleCount
);

/// <summary>
/// Break-glass de PlatformAdmin: re-publica <see cref="UserRolesChangedIntegrationEvent"/> para un
/// usuario, con su set de permisos y roles ACTUAL, para que cada microservicio re-materialice su
/// <c>UserPermissionsProjection</c> local. Existe para el modo de fallo en que el rol del usuario
/// SÍ está asignado en Auth pero su proyección quedó ausente o desincronizada en uno o más
/// servicios (evento de fan-out perdido, servicio caído durante el alta, reparación manual de DB) —
/// sin tener que reiniciar Auth para disparar <c>PermissionsBackfillService</c>, que solo repara
/// usuarios con <c>PermissionsBackfilledAt == null</c>.
///
/// <para>
/// Reproduce exactamente la lógica probada de <c>PermissionsBackfillService</c> (mismo evento
/// dirigido, misma versión): NO bumpea <c>PermissionsVersion</c> a propósito — re-proyectar a la
/// versión vigente no invalida los JWT ya emitidos (evita re-login forzado) y el consumer aplica
/// igual con versión igual o mayor. Si el usuario no tuviera roles, el set re-proyectado sería
/// vacío (no dañino); el <see cref="ReprojectUserPermissionsResult.PermissionCodeCount"/> = 0 en
/// la respuesta es la señal de que el problema real es de asignación de rol, no de proyección.
/// </para>
/// </summary>
public static class ReprojectUserPermissionsHandler
{
    public static async Task<Result<ReprojectUserPermissionsResult>> Handle(
        ReprojectUserPermissionsCommand command,
        IUserRepository users,
        IRoleRepository roles,
        ITenantContext tenantContext,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ILogger<ReprojectUserPermissionsCommand> logger,
        CancellationToken ct
    )
    {
        var user = await users.GetByIdAsync(command.UserId, ct);
        if (user is null)
            return Result.Failure<ReprojectUserPermissionsResult>(new Error("User.NotFound", "User not found."));

        // Este handler corre en el scope de Wolverine (bus.InvokeAsync), donde ITenantContext arranca
        // vacío. Sellamos el tenant real del usuario objetivo ANTES de resolver sus roles/códigos:
        // GetUserRolesAsync/GetEffectivePermissionCodesAsync consultan Role (tenant-owned) bajo el
        // filtro global fail-closed — sin esto devolverían 0 filas. Mismo criterio que
        // PermissionsBackfillService.
        tenantContext.SetTenant(user.TenantId);

        var userRoles = await roles.GetUserRolesAsync(user.Id, ct);
        var permissionCodes = await roles.GetEffectivePermissionCodesAsync(user.Id, ct);

        await bus.PublishAsync(
            new UserRolesChangedIntegrationEvent
            {
                TenantId = user.TenantId,
                UserId = user.Id,
                PermissionsVersion = user.PermissionsVersion,
                RoleNames = userRoles.Select(role => role.Name).ToArray(),
                RoleIds = userRoles.Select(role => role.Id).ToArray(),
                PermissionCodes = permissionCodes.ToArray(),
                ActorType = user.ActorType.ToString(),
                CorrelationId = correlation.CorrelationId,
            }
        );

        user.MarkPermissionsBackfilled(DateTime.UtcNow);
        await unitOfWork.SaveChangesAsync(ct);

        logger.LogInformation(
            "ReprojectUserPermissions: re-published UserRolesChanged for {UserId} (tenant {TenantId}) at version {Version} with {CodeCount} permission code(s).",
            user.Id,
            user.TenantId,
            user.PermissionsVersion,
            permissionCodes.Count
        );

        return Result.Success(
            new ReprojectUserPermissionsResult(
                user.Id,
                user.TenantId,
                user.PermissionsVersion,
                permissionCodes.Count,
                userRoles.Count
            )
        );
    }
}
