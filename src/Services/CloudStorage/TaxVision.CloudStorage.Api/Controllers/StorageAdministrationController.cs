using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.CloudStorage.Application.Administration;
using Wolverine;

namespace TaxVision.CloudStorage.Api.Controllers;

[ApiController]
[Route("storage")]
[Authorize]
[AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
public sealed class StorageAdministrationController(IMessageBus bus) : ControllerBase
{
    [HttpGet("usage")]
    [HasPermission(CloudStoragePermissions.SettingsManage)]
    [RateLimit("cloudstorage.f.admin_read")]
    [ProducesResponseType<StorageUsageResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUsage(CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<StorageUsageResponse>>(new GetStorageUsageQuery(tenantId), ct);
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpGet("audit")]
    [HasPermission(CloudStoragePermissions.AuditView)]
    [RateLimit("cloudstorage.f.admin_read")]
    [ProducesResponseType<IReadOnlyList<AuditEntryResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAudit(
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken ct = default
    )
    {
        if (!User.TryGetTenantId(out var tenantId))
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
    [HasPermission(CloudStoragePermissions.SettingsManage)]
    [RateLimit("cloudstorage.g.settings_manage")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SetPublicSharingPolicy(SetPublicSharingPolicyRequest request, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(new SetPublicSharingPolicyCommand(tenantId, request.Allow), ct);
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>
    /// Fase 3 — backfill idempotente (una sola vez): coloca en su carpeta de sistema los archivos
    /// historicos que quedaron sin carpeta (guardados M2M antes del fix). Alcance:
    /// <list type="bullet">
    /// <item>Sin <c>tenantId</c> + PlatformAdmin → barre TODOS los tenants que lo necesiten.</item>
    /// <item>Con <c>tenantId</c> → ese tenant (PlatformAdmin para uno ajeno; el resto solo el propio).</item>
    /// <item>Sin <c>tenantId</c> + no-PlatformAdmin → su propio tenant.</item>
    /// </list>
    /// <c>dryRun=true</c> (default) solo devuelve la foto sin mutar; <c>dryRun=false</c> aplica.
    /// Reejecutable sin duplicar.
    /// </summary>
    [HttpPost("admin/backfill-system-folders")]
    [HasPermission(CloudStoragePermissions.SettingsManage)]
    [RateLimit("cloudstorage.g.settings_manage")]
    [ProducesResponseType<BackfillSystemFoldersReport>(StatusCodes.Status200OK)]
    public async Task<IActionResult> BackfillSystemFolders(
        [FromQuery] Guid? tenantId = null,
        [FromQuery] bool dryRun = true,
        [FromQuery] int batchSize = 200,
        CancellationToken ct = default
    )
    {
        if (!User.TryGetTenantId(out var callerTenantId))
            return Unauthorized();

        Guid? scope;
        if (tenantId is { } requested)
        {
            // Apuntar a un tenant ajeno requiere PlatformAdmin.
            if (requested != callerTenantId && !User.IsPlatformAdmin())
                return Forbid();
            scope = requested;
        }
        else
        {
            // Sin tenant: PlatformAdmin barre todos; el resto solo el propio.
            scope = User.IsPlatformAdmin() ? null : callerTenantId;
        }

        var result = await bus.InvokeAsync<Result<BackfillSystemFoldersReport>>(
            new BackfillSystemFoldersCommand(scope, dryRun, batchSize),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
