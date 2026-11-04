using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Tenant.Api.Common;
using TaxVision.Tenant.Api.Contracts;
using TaxVision.Tenant.Application.Compliance;
using TaxVision.Tenant.Application.Compliance.Commands;
using TaxVision.Tenant.Application.Compliance.Queries;
using TaxVision.Tenant.Domain.Compliance;
using Wolverine;

namespace TaxVision.Tenant.Api.Controllers;

/// <summary>
/// Cumplimiento de la oficina del que llama: solo administradores. Doble candado: actor
/// <c>TenantAdmin</c> (un empleado con un rol custom que tenga <c>settings.manage</c> no entra) y el
/// permiso <c>settings.manage</c> (la capa de denegación de RBAC sigue mandando sobre un co-admin).
/// </summary>
[ApiController]
[Route("tenants/current/compliance")]
[Authorize]
[AllowActorTypes(ActorType.TenantAdmin)]
public sealed class OfficeComplianceController(IMessageBus bus) : ControllerBase
{
    [HttpGet]
    [HasPermission(OfficeSettingsPermissions.Manage)]
    [RateLimit("tenant.f.branding_read")]
    [ProducesResponseType<OfficeComplianceResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Forbid();

        return Ok(await bus.InvokeAsync<OfficeComplianceResponse>(new GetOfficeComplianceQuery(tenantId), ct));
    }

    [HttpPut]
    [HasPermission(OfficeSettingsPermissions.Manage)]
    [RateLimit("tenant.g.branding_manage")]
    [ProducesResponseType<OfficeComplianceResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Update([FromBody] UpdateOfficeComplianceRequest request, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId) || !User.TryGetUserId(out var actorId))
            return Forbid();

        var result = await bus.InvokeAsync<Result<OfficeComplianceResponse>>(
            new UpdateOfficeComplianceCommand(tenantId, actorId, request.Efin, request.ToDraft()),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>EFIN completo. Cada llamada queda registrada (quién, cuándo) antes de responder.</summary>
    [HttpPost("efin/reveal")]
    [HasPermission(OfficeSettingsPermissions.Manage)]
    [RateLimit("tenant.g.branding_manage")]
    [ProducesResponseType<RevealedEfinResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<Error>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> RevealEfin(CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId) || !User.TryGetUserId(out var actorId))
            return Forbid();

        var result = await bus.InvokeAsync<Result<RevealedEfinResponse>>(
            new RevealOfficeEfinCommand(tenantId, actorId),
            ct
        );
        if (result.IsSuccess)
            return Ok(result.Value);

        // ErrorHttpMapping (BuildingBlocks) no conoce este código: "sin EFIN" es un 404, no un 400.
        return result.Error == OfficeComplianceErrors.EfinNotSet
            ? NotFound(result.Error)
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
