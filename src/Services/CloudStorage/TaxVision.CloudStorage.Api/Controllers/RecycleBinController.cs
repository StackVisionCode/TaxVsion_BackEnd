using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Common;
using BuildingBlocks.Results;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.CloudStorage.Api.Common;
using TaxVision.CloudStorage.Application.Abstractions;
using TaxVision.CloudStorage.Application.Files;
using TaxVision.CloudStorage.Application.Files.RecycleBin;
using Wolverine;

namespace TaxVision.CloudStorage.Api.Controllers;

/// <summary>
/// Fase C1 — papelera: listar, restaurar y purgar manualmente. Todo bajo
/// cloudstorage.recyclebin.manage, que no es asignable a roles de portal del
/// cliente (ver PermissionCatalog en Auth) — un customer nunca llega aca.
/// </summary>
[ApiController]
[Route("storage/recycle-bin")]
[HasPermission(CloudStoragePermissions.RecycleBinManage)]
[AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
public sealed class RecycleBinController(IMessageBus bus, ICorrelationContext correlation) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<RecycleBinItemResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> List(
        [FromQuery] int skip = 0,
        [FromQuery] int take = 50,
        CancellationToken ct = default
    )
    {
        if (!User.TryGet(out var tenantId, out _, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<IReadOnlyList<RecycleBinItemResponse>>(
            new GetRecycleBinQuery(tenantId, skip, take),
            ct
        );
        return Ok(result);
    }

    [HttpPost("restore/{fileId:guid}")]
    [ProducesResponseType<FileResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Restore(Guid fileId, CancellationToken ct)
    {
        if (!User.TryGet(out var tenantId, out var actorId, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<FileResponse>>(
            new RestoreFileCommand(tenantId, actorId, fileId, AuditContext()),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpDelete("empty")]
    [ProducesResponseType<int>(StatusCodes.Status200OK)]
    public async Task<IActionResult> EmptyBin(CancellationToken ct)
    {
        if (!User.TryGet(out var tenantId, out var actorId, out _))
            return Unauthorized();

        var purgedCount = await bus.InvokeAsync<int>(new EmptyRecycleBinCommand(tenantId, actorId, AuditContext()), ct);
        return Ok(new { purgedCount });
    }

    private RequestAuditContext AuditContext() =>
        new(
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            Request.Headers.UserAgent.ToString(),
            correlation.CorrelationId
        );
}
