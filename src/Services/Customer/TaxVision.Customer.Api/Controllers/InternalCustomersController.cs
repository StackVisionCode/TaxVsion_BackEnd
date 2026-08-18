using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Common;
using BuildingBlocks.Tenancy;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Customer.Application.Customers;
using TaxVision.Customer.Application.Customers.Queries.Reconciliation;
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
[Route("internal/customers")]
public sealed class InternalCustomersController(IMessageBus bus) : ControllerBase
{
    [HttpGet("list")]
    [RateLimitExempt(
        "M2M interno entre microservicios (actor_type=Service), nunca expuesto en el Gateway público — no aplica el mismo cupo per-user/tenant que un endpoint humano."
    )]
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

    /// <summary>
    /// Enumeración CROSS-TENANT de customers para que los microservicios con proyecciones locales
    /// (Signature/Communication/Notes/Correspondence) se auto-reconcilien contra la fuente autoritativa.
    /// A diferencia de <see cref="List"/> (por tenant del token), este endpoint devuelve TODOS los
    /// tenants — por eso solo lo acepta el token de la <see cref="PlatformTenant"/> (Service). Cierra la
    /// deuda de raíz: ningún servicio podía enumerar todos los customers, así que sus proyecciones
    /// quedaban cortas cuando se perdían eventos o el servicio nació después de crear customers.
    /// Nunca expuesto en el Gateway público.
    /// </summary>
    [HttpGet("reconciliation")]
    [RateLimitExempt(
        "M2M interno de reconciliación (actor_type=Service, solo PlatformTenant), nunca expuesto en el Gateway público."
    )]
    [ProducesResponseType<PagedResult<CustomerReconciliationResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Reconciliation(
        [FromQuery] CustomerStatusFilter status = CustomerStatusFilter.Active,
        [FromQuery] int page = 1,
        [FromQuery] int size = 200,
        CancellationToken ct = default
    )
    {
        // Gate cross-tenant: solo el token de servicio de la PlatformTenant puede enumerar todos los
        // tenants. Un token de servicio de un tenant normal queda excluido aquí.
        if (!User.TryGetTenantId(out var tenantId) || tenantId != PlatformTenant.Id)
            return Forbid();

        var result = await bus.InvokeAsync<PagedResult<CustomerReconciliationResponse>>(
            new ReconciliationCustomersQuery(status, page, size),
            ct
        );
        return Ok(result);
    }
}
