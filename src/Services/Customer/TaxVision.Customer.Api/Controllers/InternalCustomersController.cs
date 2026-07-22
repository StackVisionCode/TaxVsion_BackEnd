using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Customer.Application.Customers;
using TaxVision.Customer.Application.Customers.Queries.Search;
using Wolverine;

namespace TaxVision.Customer.Api.Controllers;

/// <summary>
/// M2M interno — solo otros microservicios (token con <c>actor_type=Service</c>, policy
/// "ServiceOnly"), nunca un usuario humano. Nunca se expone en las rutas públicas del Gateway.
///
/// <para>
/// Gap real encontrado implementando Correspondence Fase 2: <c>GET /customers</c> exige
/// <c>[Authorize(Roles = "TenantEmployee,TenantAdmin")]</c>, pero los tokens M2M nunca llevan
/// claim <c>Roles</c> — solo <c>actor_type=Service</c> + <c>perm</c>. Este endpoint reusa el
/// mismo <see cref="SearchCustomersQuery"/>/handler que el endpoint público — misma lógica de
/// negocio, solo cambia el gate de autorización y de dónde sale el tenantId (siempre del token
/// de servicio, nunca de un parámetro del caller). Mismo patrón que
/// <c>Postmaster.CorrespondenceMessagesController</c> (Fase Postmaster 5).
/// </para>
/// </summary>
[ApiController]
[Authorize(Policy = "ServiceOnly")]
[AllowActorTypes(ActorType.Service)]
[Route("customers/internal")]
public sealed class InternalCustomersController(IMessageBus bus) : ControllerBase
{
    [HttpGet("list")]
    [ProducesResponseType<PagedResult<CustomerSummaryResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] string? term = null,
        [FromQuery] CustomerStatusFilter status = CustomerStatusFilter.Active,
        [FromQuery] int page = 1,
        [FromQuery] int size = 20,
        CancellationToken ct = default
    )
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Forbid();

        var result = await bus.InvokeAsync<PagedResult<CustomerSummaryResponse>>(
            new SearchCustomersQuery(tenantId, term, status, page, size),
            ct
        );
        return Ok(result);
    }
}
