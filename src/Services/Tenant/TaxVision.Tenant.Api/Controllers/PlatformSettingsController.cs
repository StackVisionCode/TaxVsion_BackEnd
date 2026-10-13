using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.Identity;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Tenant.Application.Platform;
using TaxVision.Tenant.Application.Platform.Commands;
using TaxVision.Tenant.Application.Platform.Queries;
using Wolverine;

namespace TaxVision.Tenant.Api.Controllers;

/// <summary>
/// Marca y dominio de la PLATAFORMA (nombre comercial, razón social, dominio base, host de la API,
/// correos de contacto). Solo el PlatformAdmin, con el mismo permiso que la marca visual del sistema
/// (<c>platform.branding.manage</c>, PlatformOnly). Cada cambio se publica a todos los servicios.
///
/// <para>Cambiar el dominio acá NO mueve DNS, certificados ni las URLs registradas en Google, Microsoft,
/// Stripe o el proveedor de correo: eso tiene que estar listo antes, o los links salen a un host
/// que no responde.</para>
/// </summary>
[ApiController]
[Route("platform/settings")]
[AllowActorTypes(ActorType.PlatformAdmin)]
public sealed class PlatformSettingsController(IMessageBus bus, ILogger<PlatformSettingsController> logger)
    : ControllerBase
{
    [HttpGet]
    [HasPermission(TenantBrandingPermissions.Platform)]
    [RateLimit("tenant.f.branding_read")]
    [ProducesResponseType<PlatformSettingsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<Error>(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<PlatformSettingsResponse>>(new GetPlatformSettingsQuery(), ct);
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPut]
    [HasPermission(TenantBrandingPermissions.Platform)]
    [RateLimit("tenant.g.branding_manage")]
    [ProducesResponseType<PlatformSettingsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<Error>(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Update([FromBody] UpdatePlatformSettingsRequest request, CancellationToken ct)
    {
        if (!User.TryGetUserId(out var actorId))
            return Forbid();

        var result = await bus.InvokeAsync<Result<PlatformSettingsResponse>>(
            new UpdatePlatformSettingsCommand(
                actorId,
                request.BrandName,
                request.LegalName,
                request.BaseDomain,
                request.ApiHost,
                request.SupportEmail,
                request.BillingEmail
            ),
            ct
        );
        if (result.IsFailure)
            return StatusCode(result.Error.ToHttpStatusCode(), result.Error);

        // Config global de la plataforma: queda en el log quién la cambió y a qué versión.
        logger.LogWarning(
            "Platform settings changed to v{Version} (brand={BrandName}, domain={BaseDomain}, api={ApiHost}) "
                + "by platformAdmin={ActorId}",
            result.Value.Version,
            result.Value.BrandName,
            result.Value.BaseDomain,
            result.Value.ApiHost,
            actorId
        );
        return Ok(result.Value);
    }
}

public sealed record UpdatePlatformSettingsRequest(
    string BrandName,
    string LegalName,
    string BaseDomain,
    string ApiHost,
    string SupportEmail,
    string BillingEmail
);
