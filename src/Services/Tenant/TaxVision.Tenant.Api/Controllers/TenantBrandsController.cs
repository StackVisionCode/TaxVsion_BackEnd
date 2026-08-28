using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.Identity;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Tenant.Api.Common;
using TaxVision.Tenant.Application.Brands;
using TaxVision.Tenant.Application.Brands.Commands;
using TaxVision.Tenant.Application.Brands.Queries;
using TaxVision.Tenant.Domain.Brands;
using Wolverine;

namespace TaxVision.Tenant.Api.Controllers;

/// <summary>
/// Identidad visual por superficie (TenantBrands). El TenantAdmin gestiona la marca de SU tenant
/// (colores + logo/favicon) para CRM y Portal; PlatformAdmin puede operar sobre cualquiera. Nunca
/// confía en el {tenantId} de la ruta sin verificarlo contra el JWT (TryResolveTenantId). La
/// {surface} y la {assetKey} se validan contra su vocabulario cerrado → 400, no 500.
/// Modelo nuevo, en paralelo al TenantBrandingController viejo hasta el cutover.
/// </summary>
[ApiController]
[Route("tenants/{tenantId:guid}/brands")]
public sealed class TenantBrandsController(IMessageBus bus) : ControllerBase
{
    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png",
        "image/jpeg",
        "image/svg+xml",
    };

    // ----- Lectura (cualquier usuario autenticado del tenant: necesita la marca para pintar) -----

    [HttpGet]
    [Microsoft.AspNetCore.Authorization.Authorize]
    [AllowActorTypes(
        ActorType.TenantEmployee,
        ActorType.TenantAdmin,
        ActorType.CustomerPortal,
        ActorType.PlatformAdmin
    )]
    [RateLimit("tenant.f.branding_read")]
    [ProducesResponseType<TenantBrandsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> List(Guid tenantId, CancellationToken ct)
    {
        if (!this.TryResolveTenantId(tenantId, out var resolvedTenantId))
            return Forbid();

        var result = await bus.InvokeAsync<Result<TenantBrandsResponse>>(
            new GetTenantBrandsQuery(resolvedTenantId),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpGet("{surface}")]
    [Microsoft.AspNetCore.Authorization.Authorize]
    [AllowActorTypes(
        ActorType.TenantEmployee,
        ActorType.TenantAdmin,
        ActorType.CustomerPortal,
        ActorType.PlatformAdmin
    )]
    [RateLimit("tenant.f.branding_read")]
    [ProducesResponseType<BrandResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Get(Guid tenantId, string surface, CancellationToken ct)
    {
        if (!this.TryResolveTenantId(tenantId, out var resolvedTenantId))
            return Forbid();
        if (!BrandSurfaces.TryParseConfigurable(surface, out var parsedSurface))
            return BadRequest(InvalidSurface);

        var result = await bus.InvokeAsync<Result<BrandResponse>>(
            new GetTenantBrandQuery(resolvedTenantId, parsedSurface),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    // ----- Colores (branding.manage) -----

    [HttpPut("{surface}/colors")]
    [HasPermission(TenantBrandingPermissions.Manage)]
    [AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
    [RateLimit("tenant.g.branding_manage")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> UpdateColors(
        Guid tenantId,
        string surface,
        [FromBody] UpdateBrandColorsRequest request,
        CancellationToken ct
    )
    {
        if (!this.TryResolveTenantId(tenantId, out var resolvedTenantId))
            return Forbid();
        if (!BrandSurfaces.TryParseConfigurable(surface, out var parsedSurface))
            return BadRequest(InvalidSurface);

        var result = await bus.InvokeAsync<Result>(
            new UpdateTenantBrandColorsCommand(resolvedTenantId, parsedSurface, request.Primary, request.Accent),
            ct
        );
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpDelete("{surface}/colors")]
    [HasPermission(TenantBrandingPermissions.Manage)]
    [AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
    [RateLimit("tenant.g.branding_manage")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ResetColors(Guid tenantId, string surface, CancellationToken ct)
    {
        if (!this.TryResolveTenantId(tenantId, out var resolvedTenantId))
            return Forbid();
        if (!BrandSurfaces.TryParseConfigurable(surface, out var parsedSurface))
            return BadRequest(InvalidSurface);

        var result = await bus.InvokeAsync<Result>(
            new ResetTenantBrandColorsCommand(resolvedTenantId, parsedSurface),
            ct
        );
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    // ----- Assets: logo y favicon (branding.manage) -----

    [HttpPut("{surface}/assets/{assetKey}")]
    [HasPermission(TenantBrandingPermissions.Manage)]
    [AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
    [RateLimit("tenant.i.logo_upload")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(TenantBrand.MaxAssetSizeBytes)]
    [ProducesResponseType<UploadTenantBrandAssetResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> UploadAsset(
        Guid tenantId,
        string surface,
        string assetKey,
        IFormFile file,
        CancellationToken ct
    )
    {
        if (!this.TryResolveTenantId(tenantId, out var resolvedTenantId))
            return Forbid();
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
                resolvedTenantId,
                parsedSurface,
                parsedKey,
                actorId,
                stream.ToArray(),
                file.ContentType,
                file.FileName
            ),
            ct
        );
        return result.IsSuccess ? Accepted(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpDelete("{surface}/assets/{assetKey}")]
    [HasPermission(TenantBrandingPermissions.Manage)]
    [AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
    [RateLimit("tenant.g.branding_manage")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> RemoveAsset(Guid tenantId, string surface, string assetKey, CancellationToken ct)
    {
        if (!this.TryResolveTenantId(tenantId, out var resolvedTenantId))
            return Forbid();
        if (!BrandSurfaces.TryParseConfigurable(surface, out var parsedSurface))
            return BadRequest(InvalidSurface);
        if (!BrandAssetKeys.TryParse(assetKey, out var parsedKey))
            return BadRequest(InvalidAssetKey);

        var result = await bus.InvokeAsync<Result>(
            new RemoveTenantBrandAssetCommand(resolvedTenantId, parsedSurface, parsedKey),
            ct
        );
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    // ----- Mantenimiento completo: reset de la superficie entera (branding.manage) -----

    [HttpPost("{surface}/reset")]
    [HasPermission(TenantBrandingPermissions.Manage)]
    [AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
    [RateLimit("tenant.g.branding_manage")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ResetSurface(Guid tenantId, string surface, CancellationToken ct)
    {
        if (!this.TryResolveTenantId(tenantId, out var resolvedTenantId))
            return Forbid();
        if (!BrandSurfaces.TryParseConfigurable(surface, out var parsedSurface))
            return BadRequest(InvalidSurface);

        var result = await bus.InvokeAsync<Result>(
            new ResetTenantBrandSurfaceCommand(resolvedTenantId, parsedSurface),
            ct
        );
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    private static Error InvalidSurface => new("TenantBrand.Surface", "Surface must be one of: Crm, Portal.");

    private static Error InvalidAssetKey => new("TenantBrand.Asset.Key", "Asset key must be one of: Logo, Favicon.");
}

/// <summary>Un campo en <c>null</c> = volver al default para ese token.</summary>
public sealed record UpdateBrandColorsRequest(string? Primary, string? Accent);
