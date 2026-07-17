using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.CloudStorage.Application.Administration;
using Wolverine;

namespace TaxVision.CloudStorage.Api.Controllers;

[ApiController]
[Route("storage")]
[Authorize]
public sealed class StorageAdministrationController(IMessageBus bus) : ControllerBase
{
    [HttpGet("usage")]
    [Authorize(Policy = CloudStoragePermissions.SettingsManage)]
    [ProducesResponseType<StorageUsageResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUsage(CancellationToken ct)
    {
        if (!TryGetTenant(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<StorageUsageResponse>>(new GetStorageUsageQuery(tenantId), ct);
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpGet("audit")]
    [Authorize(Policy = CloudStoragePermissions.AuditView)]
    [ProducesResponseType<IReadOnlyList<AuditEntryResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAudit(
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken ct = default
    )
    {
        if (!TryGetTenant(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<IReadOnlyList<AuditEntryResponse>>(
            new ListStorageAuditQuery(tenantId, skip, take),
            ct
        );
        return Ok(result);
    }

    public sealed record SetPublicSharingPolicyRequest(bool Allow);

    /// <summary>Fase C3 — habilita/deshabilita links Visibility.Public. Deshabilitado por defecto (datos fiscales).</summary>
    [HttpPut("settings/public-sharing")]
    [Authorize(Policy = CloudStoragePermissions.SettingsManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SetPublicSharingPolicy(SetPublicSharingPolicyRequest request, CancellationToken ct)
    {
        if (!TryGetTenant(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(new SetPublicSharingPolicyCommand(tenantId, request.Allow), ct);
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    private bool TryGetTenant(out Guid tenantId) => Guid.TryParse(User.FindFirst("tenant_id")?.Value, out tenantId);
}
