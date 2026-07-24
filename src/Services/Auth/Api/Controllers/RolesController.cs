using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Auth.Api.Common;
using TaxVision.Auth.Application.Roles.Commands;
using TaxVision.Auth.Application.Roles.Queries;
using TaxVision.Auth.Domain.Roles;
using TaxVision.Auth.Domain.Users;
using Wolverine;

namespace TaxVision.Auth.Api.Controllers;

/// <summary>
/// Gestión de roles y catálogo de permisos del tenant: listar, crear, actualizar,
/// asignar permisos y desactivar roles. Requiere el permiso de administración de roles.
/// </summary>
[ApiController]
[Route("auth/roles")]
[HasPermission(PermissionCatalog.RolesManage)]
[AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
public sealed class RolesController(IMessageBus bus) : ControllerBase
{
    /// <summary>Lista los roles definidos en el tenant del usuario autenticado.</summary>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<RoleResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRoles(CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<IReadOnlyList<RoleResponse>>>(new GetRolesQuery(tenantId), ct);

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>Devuelve el catálogo global de permisos disponibles para asignar a roles.</summary>
    [HttpGet("/auth/permissions")]
    [ProducesResponseType<IReadOnlyList<PermissionResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPermissionsCatalog(CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<IReadOnlyList<PermissionResponse>>>(
            new GetPermissionsCatalogQuery(),
            ct
        );

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>
    /// Datos de entrada para crear un rol: nombre, descripción, permisos iniciales y, opcional,
    /// el actor type destino (RBAC Fase 3 — null se valida contra staff por defecto, ver
    /// <see cref="TaxVision.Auth.Application.Common.ActorTypeRoleGuard.ValidatePermissionsForActorType"/>).
    /// </summary>
    public sealed record CreateRoleRequest(
        string Name,
        string? Description,
        IReadOnlyList<Guid> PermissionIds,
        UserActorType? TargetActorType = null
    );

    /// <summary>Crea un nuevo rol en el tenant con los permisos indicados.</summary>
    [HttpPost]
    [ProducesResponseType<RoleResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(CreateRoleRequest request, CancellationToken ct)
    {
        if (!User.TryGetUserId(out var userId) || !User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<RoleResponse>>(
            new CreateRoleCommand(
                tenantId,
                userId,
                request.Name,
                request.Description,
                request.PermissionIds,
                request.TargetActorType,
                User.GetActorType() == ActorType.PlatformAdmin
            ),
            ct
        );

        return result.IsSuccess
            ? Created($"/auth/roles/{result.Value.Id}", result.Value)
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>Datos de entrada para actualizar el nombre y la descripción de un rol.</summary>
    public sealed record UpdateRoleRequest(string Name, string? Description);

    /// <summary>Actualiza el nombre y la descripción de un rol existente.</summary>
    [HttpPut("{roleId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Update(Guid roleId, UpdateRoleRequest request, CancellationToken ct)
    {
        if (!User.TryGetUserId(out var userId) || !User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(
            new UpdateRoleCommand(tenantId, roleId, userId, request.Name, request.Description),
            ct
        );

        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>Datos de entrada para reemplazar el conjunto de permisos de un rol.</summary>
    public sealed record SetPermissionsRequest(IReadOnlyList<Guid> PermissionIds);

    /// <summary>Reemplaza por completo el conjunto de permisos asignados a un rol.</summary>
    [HttpPut("{roleId:guid}/permissions")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SetPermissions(Guid roleId, SetPermissionsRequest request, CancellationToken ct)
    {
        if (!User.TryGetUserId(out var userId) || !User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(
            new SetRolePermissionsCommand(tenantId, roleId, userId, request.PermissionIds),
            ct
        );

        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>Desactiva un rol para que deje de aplicarse sin eliminarlo del historial.</summary>
    [HttpDelete("{roleId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Deactivate(Guid roleId, CancellationToken ct)
    {
        if (!User.TryGetUserId(out var userId) || !User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(new DeactivateRoleCommand(tenantId, roleId, userId), ct);

        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
