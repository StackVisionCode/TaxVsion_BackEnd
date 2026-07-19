using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TaxVision.Tenant.Api.Authorization;
using TaxVision.Tenant.Api.Common;
using TaxVision.Tenant.Application.Tenants.Commands;
using TaxVision.Tenant.Application.Tenants.Queries;
using TaxVision.Tenant.Domain;
using Wolverine;

namespace TaxVision.Tenant.Api.Controllers;

/// <summary>
/// Soporte de logo por tenant (Tenant_Service_LogoSupport_Plan.md). PlatformAdmin puede operar
/// sobre cualquier tenant; el resto solo sobre el propio (claim tenant_id del JWT) — nunca confía
/// en el {tenantId} de la ruta sin verificarlo contra el token (ver TryResolveTenantId).
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
    [EnableRateLimiting("tenant-logo-upload")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(TaxVision.Tenant.Domain.Tenant.MaxLogoSizeBytes)]
    [ProducesResponseType<UploadTenantLogoResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Upload(Guid tenantId, IFormFile file, CancellationToken ct)
    {
        if (!TryResolveTenantId(tenantId, out var resolvedTenantId))
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
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Remove(Guid tenantId, CancellationToken ct)
    {
        if (!TryResolveTenantId(tenantId, out var resolvedTenantId))
            return Forbid();

        var result = await bus.InvokeAsync<Result>(new RemoveTenantLogoCommand(resolvedTenantId), ct);
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>Sin gate de permiso propio: cualquier usuario autenticado del tenant puede ver su logo (mismo criterio que GetTenantPublicInfo).</summary>
    [HttpGet]
    [Microsoft.AspNetCore.Authorization.Authorize]
    [ProducesResponseType<TenantLogoResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<Error>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(Guid tenantId, CancellationToken ct)
    {
        if (!TryResolveTenantId(tenantId, out var resolvedTenantId))
            return Forbid();

        var result = await bus.InvokeAsync<Result<TenantLogoResponse>>(new GetTenantLogoQuery(resolvedTenantId), ct);
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>PlatformAdmin puede operar sobre cualquier tenant; el resto solo sobre el propio (claim del JWT).</summary>
    private bool TryResolveTenantId(Guid requestedTenantId, out Guid tenantId)
    {
        tenantId = Guid.Empty;
        if (!User.TryGetTenantId(out var tokenTenantId) && !User.IsInRole("PlatformAdmin"))
            return false;

        if (User.IsInRole("PlatformAdmin") || requestedTenantId == tokenTenantId)
        {
            tenantId = requestedTenantId;
            return true;
        }

        return false;
    }
}
