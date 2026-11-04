using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Tenant.Api.Common;
using TaxVision.Tenant.Api.Contracts;
using TaxVision.Tenant.Application.Profiles;
using TaxVision.Tenant.Application.Profiles.Commands;
using TaxVision.Tenant.Application.Profiles.Queries;
using Wolverine;

namespace TaxVision.Tenant.Api.Controllers;

/// <summary>
/// Perfil público de la oficina del que llama (siempre el <c>tenant_id</c> del JWT). Leerlo puede todo el
/// staff (lo usa, por ejemplo, la vista previa de la factura); editarlo exige <c>settings.manage</c>.
/// </summary>
[ApiController]
[Route("tenants/current/profile")]
[Authorize]
[AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin)]
public sealed class OfficeProfileController(IMessageBus bus) : ControllerBase
{
    /// <summary>Sin perfil guardado devuelve todo vacío con <c>version: 0</c>, nunca 404. También lo lee la
    /// cuenta de la landing ("Billing details"); editar sigue siendo solo desde el workspace.</summary>
    [HttpGet]
    [AllowSurface(AccessSurface.Account)]
    [RateLimit("tenant.f.branding_read")]
    [ProducesResponseType<OfficeProfileResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Forbid();

        return Ok(await bus.InvokeAsync<OfficeProfileResponse>(new GetOfficeProfileQuery(tenantId), ct));
    }

    [HttpPut]
    [HasPermission(OfficeSettingsPermissions.Manage)]
    [RateLimit("tenant.g.branding_manage")]
    [ProducesResponseType<OfficeProfileResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<Error>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Update([FromBody] UpdateOfficeProfileRequest request, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId) || !User.TryGetUserId(out var actorId))
            return Forbid();

        var result = await bus.InvokeAsync<Result<OfficeProfileResponse>>(
            new UpdateOfficeProfileCommand(tenantId, actorId, request.ToDraft()),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
