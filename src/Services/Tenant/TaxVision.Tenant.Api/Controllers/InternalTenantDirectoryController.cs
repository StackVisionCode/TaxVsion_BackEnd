using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Tenant.Application.Tenants.Commands;
using TaxVision.Tenant.Application.Tenants.Queries;
using Wolverine;

namespace TaxVision.Tenant.Api.Controllers;

/// <summary>
/// Listado M2M de oficinas (id, nombre, subdominio) para que otro servicio llene de una vez una
/// proyección local de los tenants que ya existían antes de que esa proyección existiera. Hoy lo usa
/// el backfill de arranque de Postmaster, que necesita el nombre de la oficina para el From.
///
/// <para><b>Por qué un endpoint y no republicar <c>TenantCreatedIntegrationEvent</c>:</b> ese evento
/// lo consumen Auth, Subscription, PaymentApp, PaymentClient y Signature, y no es una notificación
/// inerte — dispara la invitación del admin, el trial de la suscripción y el aprovisionamiento de
/// almacenamiento. Reemitirlo para rellenar una tabla le mandaría a cada cliente existente una
/// invitación nueva y le reiniciaría el trial. Un listado de solo lectura no puede hacer ningún daño.</para>
///
/// <para>Separado de <c>InternalTenantProvisioningController</c> a propósito: aquello crea tenants,
/// esto solo lee. Mezclar un GET inocuo con el endpoint que provisiona hace más fácil equivocarse al
/// revisar permisos.</para>
/// </summary>
[ApiController]
[Route("internal/tenants")]
[Authorize(Policy = "ServiceOnly")]
[AllowActorTypes(ActorType.Service)]
public sealed class InternalTenantDirectoryController(IMessageBus bus) : ControllerBase
{
    [HttpGet("directory")]
    [RateLimitExempt("M2M interno (actor_type=Service), nunca expuesto en el Gateway público.")]
    [ProducesResponseType<IReadOnlyList<TenantResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<TenantResponse>>> GetDirectory(
        [FromQuery] int page = 1,
        [FromQuery] int size = 100,
        CancellationToken ct = default
    )
    {
        // Mismos límites que el listado humano: el que llama pagina, no se baja todo de golpe.
        if (page < 1 || size is < 1 or > 200)
            return BadRequest(new { error = "Page must be at least 1 and size must be between 1 and 200." });

        return Ok(await bus.InvokeAsync<IReadOnlyList<TenantResponse>>(new GetTenantsQuery(page, size), ct));
    }
}
