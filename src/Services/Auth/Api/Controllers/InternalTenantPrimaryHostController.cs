using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Auth.Application.TenantDomains.Internal.Queries;
using Wolverine;

namespace TaxVision.Auth.Api.Controllers;

/// <summary>
/// Host primario (subdominio de plataforma) de un tenant — M2M-only. Lo llama Notification para
/// armar los links per-tenant de los correos: portal del cliente en {host}/portal, staff en {host}.
/// Resuelve para tenants creados antes de cualquier proyección, sin depender de que el subdominio
/// viaje en cada evento. Mismo patrón, policy y motivo que <see cref="InternalUserContactController"/>.
/// </summary>
[ApiController]
[Route("internal/tenants/{tenantId:guid}/primary-host")]
[Authorize(Policy = "ServiceOnly")]
[AllowActorTypes(ActorType.Service)]
public sealed class InternalTenantPrimaryHostController(IMessageBus bus) : ControllerBase
{
    [HttpGet]
    [RateLimitExempt(
        "M2M ServiceOnly (host primario del tenant para links de correo) — nunca expuesto al Gateway público."
    )]
    [ProducesResponseType<TenantPrimaryHostResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(Guid tenantId, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<TenantPrimaryHostResponse>>(
            new GetTenantPrimaryHostQuery(tenantId),
            ct
        );

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
