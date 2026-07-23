using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.Identity;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TaxVision.Tenant.Api.Common;
using TaxVision.Tenant.Application.Tenants.Commands;
using TaxVision.Tenant.Application.Tenants.Queries;
using TaxVision.Tenant.Domain;
using Wolverine;

namespace TaxVision.Tenant.Api.Controllers;

/// <summary>
/// Soporte de logo y colores de marca por tenant (Tenant_Service_LogoSupport_Plan.md,
/// Tenant_Branding_Colors_Plan.md). PlatformAdmin puede operar sobre cualquier tenant; el resto
/// solo sobre el propio (claim tenant_id del JWT) — nunca confía en el {tenantId} de la ruta sin
/// verificarlo contra el token (ver TryResolveTenantId).
/// </summary>
[ApiController]
[Route("tenants/{tenantId:guid}/logo")]
public sealed class TenantBrandingController(IMessageBus bus) : ControllerBase
{
    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png",
        "image/jpeg",
        "image/svg+xml",
    };

    [HttpPut]
    [HasPermission(TenantBrandingPermissions.Manage)]
    [AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
    [EnableRateLimiting("tenant-logo-upload")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(TaxVision.Tenant.Domain.Tenant.MaxLogoSizeBytes)]
    [ProducesResponseType<UploadTenantLogoResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Upload(Guid tenantId, IFormFile file, CancellationToken ct)
    {
        if (!this.TryResolveTenantId(tenantId, out var resolvedTenantId))
            return Forbid();

        if (!User.TryGetUserId(out var actorId))
            return Forbid();

        if (file is null || file.Length == 0)
            return BadRequest(new Error("Tenant.Logo.File", "File is required."));

        if (!AllowedContentTypes.Contains(file.ContentType))
        {
            return BadRequest(
                new Error(
                    "Tenant.Logo.ContentType",
                    "Logo content type must be image/png, image/jpeg, or image/svg+xml."
                )
            );
        }

        if (file.Length > TaxVision.Tenant.Domain.Tenant.MaxLogoSizeBytes)
        {
            return BadRequest(
                new Error(
                    "Tenant.Logo.SizeBytes",
                    $"Logo size must be at most {TaxVision.Tenant.Domain.Tenant.MaxLogoSizeBytes} bytes."
                )
            );
        }

        using var stream = new MemoryStream();
        await file.CopyToAsync(stream, ct);

        var result = await bus.InvokeAsync<Result<UploadTenantLogoResponse>>(
            new UploadTenantLogoCommand(resolvedTenantId, actorId, stream.ToArray(), file.ContentType, file.FileName),
            ct
        );

        return result.IsSuccess ? Accepted(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpDelete]
    [HasPermission(TenantBrandingPermissions.Manage)]
    [AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Remove(Guid tenantId, CancellationToken ct)
    {
        if (!this.TryResolveTenantId(tenantId, out var resolvedTenantId))
            return Forbid();

        var result = await bus.InvokeAsync<Result>(new RemoveTenantLogoCommand(resolvedTenantId), ct);
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>Sin gate de permiso propio: cualquier usuario autenticado del tenant puede ver su logo (mismo criterio que GetTenantPublicInfo).</summary>
    [HttpGet]
    [Microsoft.AspNetCore.Authorization.Authorize]
    [AllowActorTypes(
        ActorType.TenantEmployee,
        ActorType.TenantAdmin,
        ActorType.CustomerPortal,
        ActorType.PlatformAdmin
    )]
    [ProducesResponseType<TenantLogoResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<Error>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid tenantId, CancellationToken ct)
    {
        if (!this.TryResolveTenantId(tenantId, out var resolvedTenantId))
            return Forbid();

        var result = await bus.InvokeAsync<Result<TenantLogoResponse>>(new GetTenantLogoQuery(resolvedTenantId), ct);
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>Sin gate de permiso propio: cualquier usuario autenticado del tenant necesita los colores para pintar su pantalla (mismo criterio que Get de logo).</summary>
    [HttpGet("/tenants/{tenantId:guid}/branding/colors")]
    [Microsoft.AspNetCore.Authorization.Authorize]
    [AllowActorTypes(
        ActorType.TenantEmployee,
        ActorType.TenantAdmin,
        ActorType.CustomerPortal,
        ActorType.PlatformAdmin
    )]
    [ProducesResponseType<TenantBrandingColorsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<Error>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetColors(Guid tenantId, CancellationToken ct)
    {
        if (!this.TryResolveTenantId(tenantId, out var resolvedTenantId))
            return Forbid();

        var result = await bus.InvokeAsync<Result<TenantBrandingColorsResponse>>(
            new GetTenantBrandingColorsQuery(resolvedTenantId),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPut("/tenants/{tenantId:guid}/branding/colors")]
    [HasPermission(TenantBrandingPermissions.Manage)]
    [AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> UpdateColors(
        Guid tenantId,
        [FromBody] UpdateTenantBrandingColorsRequest request,
        CancellationToken ct
    )
    {
        if (!this.TryResolveTenantId(tenantId, out var resolvedTenantId))
            return Forbid();

        var result = await bus.InvokeAsync<Result>(
            new UpdateTenantBrandingColorsCommand(
                resolvedTenantId,
                request.PrimaryColor,
                request.AccentColor,
                request.BackgroundColor,
                request.TextColor
            ),
            ct
        );
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpDelete("/tenants/{tenantId:guid}/branding/colors")]
    [HasPermission(TenantBrandingPermissions.Manage)]
    [AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> ResetColors(Guid tenantId, CancellationToken ct)
    {
        if (!this.TryResolveTenantId(tenantId, out var resolvedTenantId))
            return Forbid();

        var result = await bus.InvokeAsync<Result>(new ResetTenantBrandingColorsCommand(resolvedTenantId), ct);
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}

/// <summary>Un campo en <c>null</c> = volver al default de la empresa para ese campo (Tenant_Branding_Colors_Plan.md §5).</summary>
public sealed record UpdateTenantBrandingColorsRequest(
    string? PrimaryColor,
    string? AccentColor,
    string? BackgroundColor,
    string? TextColor
);
