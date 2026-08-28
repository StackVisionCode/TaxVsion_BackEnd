using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.Identity;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Tenant.Application.Brands;
using TaxVision.Tenant.Application.Brands.Commands;
using TaxVision.Tenant.Application.Brands.Queries;
using TaxVision.Tenant.Domain.Brands;
using Wolverine;

namespace TaxVision.Tenant.Api.Controllers;

/// <summary>
/// Marca del SISTEMA (defaults de la plataforma por superficie) — nivel 2 de la cascada al que caen
/// todos los tenants que no personalizaron. Opera sobre las filas del tenant de plataforma reusando
/// los mismos comandos que el TenantAdmin, pero reservado al PlatformAdmin: guardrail #22 — el
/// <see cref="PlatformTenant.Id"/> es EXPLÍCITO acá, no se confía en "nadie más lo llama". Cada
/// cambio se audita por log estructurado (es config global). Un TenantAdmin recibe 403 (no tiene
/// <c>platform.branding.manage</c>, que es PlatformOnly).
/// </summary>
[ApiController]
[Route("platform/branding")]
[AllowActorTypes(ActorType.PlatformAdmin)]
public sealed class PlatformBrandingController(IMessageBus bus, ILogger<PlatformBrandingController> logger)
    : ControllerBase
{
    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png",
        "image/jpeg",
        "image/svg+xml",
    };

    // Nota de cache: cambiar un default del sistema afecta a los tenants que caen a él, pero sus
    // claves tenant:brand:{tid}:{surface} NO se invalidan aquí — el cambio se propaga en ≤5 min por
    // el TTL. Invalidación global (enumerar/versionar todas las claves) no compensa para una acción
    // de admin poco frecuente con staleness acotado (Fase 4: decidido con el coste medido, no global).

    [HttpGet("{surface}")]
    [HasPermission(TenantBrandingPermissions.Platform)]
    [RateLimit("tenant.f.branding_read")]
    [ProducesResponseType<BrandResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Get(string surface, CancellationToken ct)
    {
        if (!BrandSurfaces.TryParseConfigurable(surface, out var parsedSurface))
            return BadRequest(InvalidSurface);

        var result = await bus.InvokeAsync<Result<BrandResponse>>(
            new GetTenantBrandQuery(PlatformTenant.Id, parsedSurface),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPut("{surface}/colors")]
    [HasPermission(TenantBrandingPermissions.Platform)]
    [RateLimit("tenant.g.branding_manage")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> UpdateColors(
        string surface,
        [FromBody] UpdateBrandColorsRequest request,
        CancellationToken ct
    )
    {
        if (!BrandSurfaces.TryParseConfigurable(surface, out var parsedSurface))
            return BadRequest(InvalidSurface);

        var result = await bus.InvokeAsync<Result>(
            new UpdateTenantBrandColorsCommand(PlatformTenant.Id, parsedSurface, request.Primary, request.Accent),
            ct
        );
        if (result.IsFailure)
            return StatusCode(result.Error.ToHttpStatusCode(), result.Error);

        Audit("update-colors", parsedSurface);
        return NoContent();
    }

    [HttpPut("{surface}/assets/{assetKey}")]
    [HasPermission(TenantBrandingPermissions.Platform)]
    [RateLimit("tenant.i.logo_upload")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(TenantBrand.MaxAssetSizeBytes)]
    [ProducesResponseType<UploadTenantBrandAssetResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> UploadAsset(string surface, string assetKey, IFormFile file, CancellationToken ct)
    {
        if (!User.TryGetUserId(out var actorId))
            return Forbid();
        if (!BrandSurfaces.TryParseConfigurable(surface, out var parsedSurface))
            return BadRequest(InvalidSurface);
        if (!BrandAssetKeys.TryParse(assetKey, out var parsedKey))
            return BadRequest(InvalidAssetKey);

        if (file is null || file.Length == 0)
            return BadRequest(new Error("TenantBrand.Asset.File", "File is required."));
        if (!AllowedContentTypes.Contains(file.ContentType))
            return BadRequest(
                new Error(
                    "TenantBrand.Asset.ContentType",
                    "Asset content type must be image/png, image/jpeg, or image/svg+xml."
                )
            );
        if (file.Length > TenantBrand.MaxAssetSizeBytes)
            return BadRequest(
                new Error(
                    "TenantBrand.Asset.SizeBytes",
                    $"Asset size must be at most {TenantBrand.MaxAssetSizeBytes} bytes."
                )
            );

        using var stream = new MemoryStream();
        await file.CopyToAsync(stream, ct);

        var result = await bus.InvokeAsync<Result<UploadTenantBrandAssetResponse>>(
            new UploadTenantBrandAssetCommand(
                PlatformTenant.Id,
                parsedSurface,
                parsedKey,
                actorId,
                stream.ToArray(),
                file.ContentType,
                file.FileName
            ),
            ct
        );
        if (result.IsFailure)
            return StatusCode(result.Error.ToHttpStatusCode(), result.Error);

        Audit($"upload-asset:{parsedKey}", parsedSurface);
        return Accepted(result.Value);
    }

    [HttpDelete("{surface}/assets/{assetKey}")]
    [HasPermission(TenantBrandingPermissions.Platform)]
    [RateLimit("tenant.g.branding_manage")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> RemoveAsset(string surface, string assetKey, CancellationToken ct)
    {
        if (!BrandSurfaces.TryParseConfigurable(surface, out var parsedSurface))
            return BadRequest(InvalidSurface);
        if (!BrandAssetKeys.TryParse(assetKey, out var parsedKey))
            return BadRequest(InvalidAssetKey);

        var result = await bus.InvokeAsync<Result>(
            new RemoveTenantBrandAssetCommand(PlatformTenant.Id, parsedSurface, parsedKey),
            ct
        );
        if (result.IsFailure)
            return StatusCode(result.Error.ToHttpStatusCode(), result.Error);

        Audit($"remove-asset:{parsedKey}", parsedSurface);
        return NoContent();
    }

    /// <summary>Vuelve la superficie del sistema a las constantes compiladas (limpia colores + assets).</summary>
    [HttpPost("{surface}/reset")]
    [HasPermission(TenantBrandingPermissions.Platform)]
    [RateLimit("tenant.g.branding_manage")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ResetSurface(string surface, CancellationToken ct)
    {
        if (!BrandSurfaces.TryParseConfigurable(surface, out var parsedSurface))
            return BadRequest(InvalidSurface);

        var result = await bus.InvokeAsync<Result>(
            new ResetTenantBrandSurfaceCommand(PlatformTenant.Id, parsedSurface),
            ct
        );
        if (result.IsFailure)
            return StatusCode(result.Error.ToHttpStatusCode(), result.Error);

        Audit("reset-surface", parsedSurface);
        return NoContent();
    }

    private void Audit(string action, Domain.Enums.BrandSurface surface)
    {
        User.TryGetUserId(out var actorId);
        logger.LogInformation(
            "System branding changed: action={Action} surface={Surface} by platformAdmin={ActorId}",
            action,
            surface,
            actorId
        );
    }

    private static Error InvalidSurface => new("TenantBrand.Surface", "Surface must be one of: Crm, Portal.");

    private static Error InvalidAssetKey => new("TenantBrand.Asset.Key", "Asset key must be one of: Logo, Favicon.");
}
