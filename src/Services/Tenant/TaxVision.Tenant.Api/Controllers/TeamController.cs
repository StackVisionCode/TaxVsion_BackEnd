using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Common;
using BuildingBlocks.Contracts.Work;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.Identity;
using BuildingBlocks.Web.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Tenant.Application.Team;
using TaxVision.Tenant.Application.Team.Queries;
using Wolverine;

namespace TaxVision.Tenant.Api.Controllers;

/// <summary>
/// Módulo Team del Work Profile. La actividad propia siempre; la de otro miembro exige <c>users.work.view</c>, y el
/// recorte (clientes asignados, notas privadas, facturas) sale de los permisos del que mira. El tenant, del JWT.
/// </summary>
[ApiController]
[Route("team")]
[Authorize]
[AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
public sealed class TeamController(IMessageBus bus, IUserPermissionsSource permissions) : ControllerBase
{
    // Permiso de lectura de facturas de Billing (no está en BuildingBlocks.Authorization).
    private const string InvoicingView = "invoicing.view";

    [HttpGet("members/{userId:guid}/activity")]
    [RateLimit("tenant.f.list")]
    [ProducesResponseType<PagedResult<ActivityEntryView>>(StatusCodes.Status200OK)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<Error>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Activity(
        Guid userId,
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] string? verbs,
        [FromQuery] int? page,
        [FromQuery] int? size,
        CancellationToken ct
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var viewerId))
            return Unauthorized();

        var query = new GetMemberActivityQuery(
            tenantId,
            viewerId,
            userId,
            await RightsAsync(ct),
            from?.UtcDateTime,
            to?.UtcDateTime,
            verbs,
            page,
            size
        );
        var result = await bus.InvokeAsync<Result<PagedResult<ActivityEntryView>>>(query, ct);
        return result.IsSuccess ? Ok(result.Value) : StatusCode(StatusOf(result.Error), result.Error);
    }

    /// <summary>
    /// Registro de proveedores de trabajo. Cualquier miembro del staff: es el catálogo de rutas (sin datos) que el CRM
    /// necesita también para "My work"; cada proveedor aplica su propia autorización.
    /// </summary>
    [HttpGet("work-providers")]
    [RateLimit("tenant.f.list")]
    [ProducesResponseType<IReadOnlyList<WorkProviderDescriptor>>(StatusCodes.Status200OK)]
    public IActionResult WorkProviders() => Ok(TeamWorkProviders.All);

    /// <summary>Registro de títulos del historial (solo rutas, sin datos); cada servicio aplica su autorización.</summary>
    [HttpGet("title-providers")]
    [RateLimit("tenant.f.list")]
    [ProducesResponseType<IReadOnlyList<TitleProviderDescriptor>>(StatusCodes.Status200OK)]
    public IActionResult TitleProviders() => Ok(TeamTitleProviders.All);

    public static int StatusOf(Error error) =>
        error == TeamErrors.ActivityForbidden ? StatusCodes.Status403Forbidden : StatusCodes.Status400BadRequest;

    private async Task<ActivityViewerRights> RightsAsync(CancellationToken ct) =>
        new(
            await HasAsync(UserManagementPermissions.UsersWorkView, ct),
            await HasAsync(CustomersPermissions.ViewAll, ct),
            await HasAsync(NotesPermissions.ViewAll, ct),
            await HasAsync(InvoicingView, ct)
        );

    private Task<bool> HasAsync(string permission, CancellationToken ct) =>
        permissions.HasPermissionAsync(User, permission, ct);
}
