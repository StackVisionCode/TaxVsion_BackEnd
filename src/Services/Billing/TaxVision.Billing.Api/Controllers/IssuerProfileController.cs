using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Billing.Api.Authorization;
using TaxVision.Billing.Application.Invoices.IssuerProfile;
using Wolverine;

namespace TaxVision.Billing.Api.Controllers;

/// <summary>Datos de la empresa del tenant (emisor de las facturas), solo lectura. Desde la F1 el perfil
/// se edita en Tenant (<c>PUT /tenants/current/profile</c>) y Billing lo proyecta; lo estampa en cada
/// factura al crearla.</summary>
[ApiController]
[Route("billing/issuer-profile")]
[Authorize]
[AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
public sealed class IssuerProfileController(IMessageBus bus) : ControllerBase
{
    [HttpGet]
    [RateLimit("billing.f.issuer_profile_read")]
    [HasPermission(InvoicingPermissions.View)]
    [ProducesResponseType<IssuerProfileResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<IssuerProfileResponse>>(new GetIssuerProfileQuery(tenantId), ct);
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
