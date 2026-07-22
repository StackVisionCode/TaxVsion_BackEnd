using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TaxVision.Tenant.Api.Common;
using TaxVision.Tenant.Application.Tenants.Commands;
using TaxVision.Tenant.Application.Tenants.Queries;
using TaxVision.Tenant.Domain.Enums;
using Wolverine;

namespace TaxVision.Tenant.Api.Controllers;

/// <summary>
/// Get/ChangeStatus resuelven a PlatformAdmin bajo el nuevo filtro [AllowActorTypes]
/// (Fase 3 del plan de autorización por actor type). Create es un caso especial — ver su
/// propio doc comment.
/// </summary>
[ApiController]
[Route("tenants")]
[AllowActorTypes(ActorType.PlatformAdmin)]
public sealed class TenantController(IMessageBus bus) : ControllerBase
{
    /// <summary>
    /// Requiere una de dos cosas: el ticket firmado que Auth emite al reservar el
    /// subdominio (ReserveSubdomainHandler, claims reg_slug/reg_email), o el rol
    /// PlatformAdmin creando un tenant directamente. Ver policy "TenantRegistration".
    ///
    /// <para>
    /// <see cref="AuthorizedByCapabilityTokenAttribute"/>: el ticket es de un solo uso y
    /// no lleva claim <c>actor_type</c> por diseño (no es una identidad persistente, es un
    /// "capability token" — mismo patrón que la Tickets API de Auth0 o el authorization
    /// code de OAuth). La policy "TenantRegistration" (Capa 3, aplicada por el middleware
    /// <c>UseAuthorization()</c> ANTES de que corra cualquier filtro de MVC) ya autoriza
    /// correctamente al portador — este atributo solo exime a la acción del chequeo
    /// adicional de <see cref="ActorType"/> de la Capa 2, que sería redundante e
    /// incompatible con un token sin identidad persistente. No afecta ni relaja el
    /// <c>[Authorize(Policy = "TenantRegistration")]</c> de abajo, que sigue aplicándose
    /// sin cambios.
    /// </para>
    /// </summary>
    [HttpPost]
    [Authorize(Policy = "TenantRegistration")]
    [AuthorizedByCapabilityToken]
    [EnableRateLimiting("tenant-registration")]
    [ProducesResponseType<CreateTenantResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] CreateTenantRequest request, CancellationToken cancellationToken)
    {
        var resolved = EffectiveTenantRegistrationResolver.Resolve(User, request);
        if (resolved.IsFailure)
            return StatusCode(resolved.Error.ToHttpStatusCode(), resolved.Error);

        var command = new CreateTenantCommand(
            request.Name,
            resolved.Value.Subdomain,
            resolved.Value.AdminEmail,
            request.DefaultTimeZoneId
        );

        Result<CreateTenantResponse>? result = await bus.InvokeAsync<Result<CreateTenantResponse>>(
            command,
            cancellationToken
        );

        return result.IsSuccess
            ? Created($"/tenants/{result.Value.Id}", result.Value)
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpGet]
    [Authorize(Roles = "PlatformAdmin")]
    [ProducesResponseType<IReadOnlyList<TenantResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<TenantResponse>>> Get(
        [FromQuery] int page = 1,
        [FromQuery] int size = 20,
        CancellationToken cancellationToken = default
    )
    {
        if (page < 1 || size is < 1 or > 100)
        {
            return BadRequest(
                new Error("Tenant.Pagination", "Page must be at least 1 and size must be between 1 and 100.")
            );
        }

        var tenants = await bus.InvokeAsync<IReadOnlyList<TenantResponse>>(
            new GetTenantsQuery(page, size),
            cancellationToken
        );

        return Ok(tenants);
    }

    [HttpPatch("{tenantId:guid}/status")]
    [Authorize(Roles = "PlatformAdmin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ChangeStatus(
        Guid tenantId,
        [FromBody] ChangeTenantStatusRequest request,
        CancellationToken cancellationToken
    )
    {
        var result = await bus.InvokeAsync<Result>(
            new ChangeTenantStatusCommand(tenantId, request.Status),
            cancellationToken
        );

        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}

public sealed record ChangeTenantStatusRequest(EnumTenantStatus.TenantStatus Status);
