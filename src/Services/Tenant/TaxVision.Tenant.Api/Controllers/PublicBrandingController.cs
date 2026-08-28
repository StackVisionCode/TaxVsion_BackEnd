using BuildingBlocks.Results;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Tenant.Application.Brands;
using TaxVision.Tenant.Application.Brands.Queries;
using Wolverine;

namespace TaxVision.Tenant.Api.Controllers;

/// <summary>
/// Branding ANÓNIMO para el login (pre-auth): sirve el logo/favicon por fileId y resuelve la marca
/// por slug. Sin sesión — la única credencial es el {token} de la ruta, particionado por la policy
/// <c>tenant.d.branding_public</c> (categoría D). El param DEBE llamarse "token" o el rate limit hace
/// fail-open (ver hallazgo Fase 2). Rutas literales <c>tenants/branding/...</c>: no chocan con
/// <c>tenants/{tenantId:guid}/...</c> porque "branding" no es un guid.
/// </summary>
[ApiController]
[Route("tenants/branding")]
[AllowAnonymous]
public sealed class PublicBrandingController(IMessageBus bus) : ControllerBase
{
    /// <summary>Sirve un asset de marca (302 → presigned de CloudStorage, mismo patrón que el resto del
    /// sistema). {token} = fileId. El cache del redirect se acota a la vida real de la presigned.</summary>
    [HttpGet("assets/{token}")]
    [RateLimit("tenant.d.branding_public")]
    [ProducesResponseType(StatusCodes.Status302Found)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsset(string token, CancellationToken ct)
    {
        if (!Guid.TryParse(token, out var fileId))
            return NotFound();

        var result = await bus.InvokeAsync<Result<PublicBrandingAsset>>(new GetPublicBrandingAssetQuery(fileId), ct);
        if (result.IsFailure)
            return NotFound();

        // El destino es una presigned de vida corta: el cache del redirect NUNCA debe sobrevivir a la
        // firma (si no, el navegador reusa una firma caducada → 403 → imagen rota). Ver BrandingAssetCachePolicy.
        Response.Headers.CacheControl = BrandingAssetCachePolicy.CacheControl(
            result.Value.ExpiresAtUtc,
            DateTime.UtcNow
        );
        return Redirect(result.Value.Url.ToString());
    }

    /// <summary>Marca del SISTEMA (plataforma) para el login CENTRAL (app.*/localhost), sin oficina.</summary>
    [HttpGet("system")]
    [RateLimitExempt(
        "Marca del sistema: recurso único e igual para todos, cacheable, servido al login central. "
            + "No hay token que particionar (a diferencia de la rama por slug)."
    )]
    [ProducesResponseType<PublicBrandingResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetSystem([FromQuery] string surface, CancellationToken ct)
    {
        if (!BrandSurfaces.TryParseConfigurable(surface, out var parsedSurface))
            return BadRequest(new Error("TenantBrand.Surface", "Surface must be one of: Crm, Portal."));

        var result = await bus.InvokeAsync<Result<PublicBrandingResponse>>(
            new GetSystemBrandingQuery(parsedSurface),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>Resuelve la marca por slug para el login. {token} = slug. Siempre 200 (anti-enumeración).</summary>
    [HttpGet("public/{token}")]
    [RateLimit("tenant.d.branding_public")]
    [ProducesResponseType<PublicBrandingResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetBySlug(string token, [FromQuery] string surface, CancellationToken ct)
    {
        if (!BrandSurfaces.TryParseConfigurable(surface, out var parsedSurface))
            return BadRequest(new Error("TenantBrand.Surface", "Surface must be one of: Crm, Portal."));

        var result = await bus.InvokeAsync<Result<PublicBrandingResponse>>(
            new GetPublicBrandingBySlugQuery(token, parsedSurface),
            ct
        );
        // El handler nunca falla (slug desconocido → marca del sistema), pero mapeamos por si acaso.
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
