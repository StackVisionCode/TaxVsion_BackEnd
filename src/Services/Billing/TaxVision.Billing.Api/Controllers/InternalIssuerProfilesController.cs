using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Common;
using BuildingBlocks.Tenancy;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Billing.Application.Invoices.IssuerProfile;
using Wolverine;

namespace TaxVision.Billing.Api.Controllers;

/// <summary>
/// Listado M2M de los perfiles de emisor de todas las oficinas. Lo usa la importación única de Tenant
/// (<c>POST /platform/tenants/profiles/import-from-billing</c>) para crear los perfiles de oficina que
/// aún no existen. Solo lectura y cross-tenant: exige un token de servicio emitido para la PlatformTenant.
/// No pasa por el Gateway (no empieza por /billing).
/// </summary>
[ApiController]
[Route("internal/billing/issuer-profiles")]
[Authorize(Policy = "ServiceOnly")]
[AllowActorTypes(ActorType.Service)]
public sealed class InternalIssuerProfilesController(IMessageBus bus) : ControllerBase
{
    [HttpGet]
    [RateLimitExempt("M2M interno de importación (actor_type=Service, solo PlatformTenant); nunca en el Gateway.")]
    [ProducesResponseType<PagedResult<IssuerProfileExportItem>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int size = 100,
        CancellationToken ct = default
    )
    {
        if (!User.TryGetTenantId(out var tenantId) || tenantId != PlatformTenant.Id)
            return Forbid();

        if (page < 1 || size < 1 || size > ListIssuerProfilesHandler.MaxSize)
            return BadRequest(
                new
                {
                    error = $"Page must be at least 1 and size must be between 1 and {ListIssuerProfilesHandler.MaxSize}.",
                }
            );

        var result = await bus.InvokeAsync<PagedResult<IssuerProfileExportItem>>(
            new ListIssuerProfilesQuery(page, size),
            ct
        );
        return Ok(result);
    }
}
