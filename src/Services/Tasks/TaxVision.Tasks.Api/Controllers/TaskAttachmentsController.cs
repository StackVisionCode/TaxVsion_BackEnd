using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.Identity;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Tasks.Api.Requests;
using TaxVision.Tasks.Application.Attachments;
using TaxVision.Tasks.Application.Attachments.Commands;
using TaxVision.Tasks.Application.Attachments.Queries;
using Wolverine;

namespace TaxVision.Tasks.Api.Controllers;

/// <summary>
/// Referencias a archivos de CloudStorage. Por aquí no pasa ni un byte: el frontend sube con su
/// propio token y Task sólo guarda el id.
/// </summary>
[ApiController]
[Route("tasks/{taskId:guid}/attachments")]
[AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
public sealed class TaskAttachmentsController(IMessageBus bus) : ControllerBase
{
    /// <summary>El caso dominante: el archivo ya está en CloudStorage y ya fue escaneado.</summary>
    [HttpPost("link")]
    [HasPermission(TasksPermissions.Write)]
    [RateLimit("task.h.attachments_write")]
    public async Task<IActionResult> Link(
        Guid taskId,
        [FromBody] LinkTaskAttachmentRequest request,
        CancellationToken ct
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Forbid();

        var result = await bus.InvokeAsync<Result<TaskAttachmentResponse>>(
            new LinkTaskAttachmentCommand(
                tenantId,
                userId,
                taskId,
                request.FileId,
                request.DisplayName,
                request.ContentType,
                request.SizeBytes
            ),
            ct
        );

        return result.IsFailure ? StatusCode(result.Error.ToHttpStatusCode(), result.Error) : Ok(result.Value);
    }

    [HttpPost]
    [HasPermission(TasksPermissions.Write)]
    [RateLimit("task.h.attachments_write")]
    public async Task<IActionResult> Upload(
        Guid taskId,
        [FromBody] UploadTaskAttachmentRequest request,
        CancellationToken ct
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Forbid();

        var result = await bus.InvokeAsync<Result<TaskAttachmentResponse>>(
            new UploadTaskAttachmentCommand(
                tenantId,
                userId,
                taskId,
                request.FileId,
                request.DisplayName,
                request.ContentType,
                request.SizeBytes
            ),
            ct
        );

        return result.IsFailure ? StatusCode(result.Error.ToHttpStatusCode(), result.Error) : Ok(result.Value);
    }

    /// <summary>Quita la referencia. El archivo sigue en CloudStorage.</summary>
    [HttpDelete("{fileId:guid}")]
    [HasPermission(TasksPermissions.Write)]
    [RateLimit("task.h.attachments_write")]
    public async Task<IActionResult> Detach(Guid taskId, Guid fileId, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Forbid();

        var result = await bus.InvokeAsync<Result>(new DetachTaskAttachmentCommand(tenantId, taskId, fileId), ct);

        return result.IsFailure ? StatusCode(result.Error.ToHttpStatusCode(), result.Error) : NoContent();
    }

    [HttpGet]
    [HasPermission(TasksPermissions.Read)]
    [RateLimit("task.f.read")]
    public async Task<IActionResult> List(
        Guid taskId,
        [FromQuery] bool includeDescendants = false,
        CancellationToken ct = default
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Forbid();

        var result = await bus.InvokeAsync<Result<IReadOnlyList<TaskAttachmentResponse>>>(
            new ListTaskAttachmentsQuery(tenantId, taskId, includeDescendants),
            ct
        );

        return result.IsFailure ? StatusCode(result.Error.ToHttpStatusCode(), result.Error) : Ok(result.Value);
    }
}
