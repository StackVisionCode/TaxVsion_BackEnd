using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Tenant.Application.Profiles.Abstractions;
using TaxVision.Tenant.Application.Profiles.Commands;
using Wolverine;

namespace TaxVision.Tenant.Api.Controllers;

/// <summary>
/// Operaciones de plataforma sobre los perfiles de TODAS las oficinas. Solo PlatformAdmin, con el mismo
/// permiso PlatformOnly que cambiar el estado de cualquier oficina (<c>tenant.status.change</c>): un
/// TenantAdmin nunca lo recibe.
/// </summary>
[ApiController]
[Route("platform/tenants/profiles")]
[Authorize]
[AllowActorTypes(ActorType.PlatformAdmin)]
public sealed class PlatformOfficeProfilesController(IMessageBus bus) : ControllerBase
{
    /// <summary>
    /// Copia una vez los emisores de factura de Billing a las oficinas que aún no tienen perfil.
    /// Idempotente: se puede repetir. Responde <c>{ imported, skipped }</c>.
    /// </summary>
    [HttpPost("import-from-billing")]
    [HasPermission(TenantPermissions.StatusChange)]
    [RateLimit("tenant.g.status_change")]
    [ProducesResponseType<ImportOfficeProfilesResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<Error>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> ImportFromBilling(CancellationToken ct)
    {
        if (!User.TryGetUserId(out var actorId))
            return Forbid();

        var result = await bus.InvokeAsync<Result<ImportOfficeProfilesResponse>>(
            new ImportOfficeProfilesFromBillingCommand(actorId),
            ct
        );
        if (result.IsSuccess)
            return Ok(result.Value);

        // Billing caído no es culpa del que llama: 503 para que reintente más tarde.
        return result.Error == BillingIssuerProfileErrors.Unavailable
            ? StatusCode(StatusCodes.Status503ServiceUnavailable, result.Error)
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>
    /// Vuelve a anunciar el perfil de TODAS las oficinas para poblar o reparar las proyecciones. Se llama
    /// una vez tras desplegar los consumidores, y cuando una proyección se pierde. Idempotente.
    /// </summary>
    [HttpPost("republish")]
    [HasPermission(TenantPermissions.StatusChange)]
    [RateLimit("tenant.g.status_change")]
    [ProducesResponseType<RepublishOfficeProfilesResult>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Republish(CancellationToken ct)
    {
        if (!User.TryGetUserId(out var actorId))
            return Forbid();

        var result = await bus.InvokeAsync<Result<RepublishOfficeProfilesResult>>(
            new RepublishOfficeProfilesCommand(actorId),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
