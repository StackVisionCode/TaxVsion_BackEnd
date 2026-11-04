using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Tenant.Api.Common;
using TaxVision.Tenant.Application.Tenants.Commands;
using TaxVision.Tenant.Application.Tenants.Queries;
using Wolverine;

namespace TaxVision.Tenant.Api.Controllers;

/// <summary>
/// La oficina del que llama (siempre el <c>tenant_id</c> del JWT, nunca uno de la ruta): leer nombre,
/// subdominio y zona horaria, y editar nombre + zona horaria como estado completo.
/// </summary>
[ApiController]
[Route("tenants/current")]
[Authorize]
[AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin)]
public sealed class CurrentTenantController(IMessageBus bus) : ControllerBase
{
    /// <summary>Lectura para todo el staff de la oficina: el nombre y la zona horaria se pintan en
    /// muchas pantallas, no solo en la de ajustes.</summary>
    [HttpGet]
    [RateLimit("tenant.f.branding_read")]
    [ProducesResponseType<TenantResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<Error>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Forbid();

        var result = await bus.InvokeAsync<Result<TenantResponse>>(new GetCurrentTenantQuery(tenantId), ct);
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPut]
    [HasPermission(OfficeSettingsPermissions.Manage)]
    [RateLimit("tenant.g.branding_manage")]
    [ProducesResponseType<TenantResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<Error>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Update([FromBody] UpdateCurrentTenantRequest request, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Forbid();

        var result = await bus.InvokeAsync<Result<TenantResponse>>(
            new UpdateCurrentTenantCommand(
                tenantId,
                request.Name ?? string.Empty,
                request.DefaultTimeZoneId ?? string.Empty
            ),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}

/// <summary>Estado completo: ambos campos son obligatorios (un campo ausente es un 400, no "sin cambio").</summary>
public sealed record UpdateCurrentTenantRequest(string? Name, string? DefaultTimeZoneId);
