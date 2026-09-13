using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Scribe.Application.SystemAssets.Commands;
using Wolverine;

namespace TaxVision.Scribe.Api.Controllers;

/// <summary>
/// Assets de plataforma que se embeben en los correos del sistema. PlatformAdmin-only: el logo de
/// header aplica a TODOS los tenants (scope System), no a uno. El logo de un tenant NO se toca acá —
/// ese sale de su brand asset "CRM Logo" (Tenant service) que ya alimenta TenantLogoRef en Scribe.
/// </summary>
[ApiController]
[Route("scribe/system-assets")]
[Authorize]
[AllowActorTypes(ActorType.PlatformAdmin)]
public sealed class SystemAssetsController(IMessageBus bus) : ControllerBase
{
    /// <summary>Reemplaza el logo de header de la plataforma (PNG obligatorio — email no renderiza
    /// SVG). Multipart con el campo <c>file</c>.</summary>
    [HttpPut("header-logo")]
    [HasPermission(ScribePermissions.LayoutsWrite)]
    [RateLimit("scribe.g.layout_manage")]
    [RequestSizeLimit(2 * 1024 * 1024)]
    [ProducesResponseType<UpdateSystemHeaderLogoResult>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateHeaderLogo(IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return StatusCode(
                StatusCodes.Status400BadRequest,
                new Error("SystemAsset.Empty", "The uploaded file is empty.")
            );

        User.TryGetUserId(out var userId);

        using var memory = new MemoryStream();
        await file.CopyToAsync(memory, ct);

        var result = await bus.InvokeAsync<Result<UpdateSystemHeaderLogoResult>>(
            new UpdateSystemHeaderLogoCommand(memory.ToArray(), file.ContentType, userId),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
