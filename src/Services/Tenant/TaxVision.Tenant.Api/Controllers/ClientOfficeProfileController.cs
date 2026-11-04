using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Tenant.Application.Profiles;
using TaxVision.Tenant.Application.Profiles.Queries;
using Wolverine;

namespace TaxVision.Tenant.Api.Controllers;

/// <summary>
/// La oficina vista por el cliente del portal: solo lo público (nombre comercial, contacto, dirección,
/// horario y enlaces). El tenant sale siempre del JWT; nunca expone datos legales ni de cumplimiento.
/// </summary>
[ApiController]
[Route("tenants/current/client-profile")]
[Authorize]
[AllowActorTypes(ActorType.CustomerPortal)]
public sealed class ClientOfficeProfileController(IMessageBus bus) : ControllerBase
{
    /// <summary>Sin perfil guardado responde solo el nombre de la oficina y su zona horaria.</summary>
    [HttpGet]
    [RateLimit("tenant.f.portal_profile_read")]
    [ProducesResponseType<ClientOfficeProfileResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<Error>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Forbid();

        var result = await bus.InvokeAsync<Result<ClientOfficeProfileResponse>>(
            new GetClientOfficeProfileQuery(tenantId),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
