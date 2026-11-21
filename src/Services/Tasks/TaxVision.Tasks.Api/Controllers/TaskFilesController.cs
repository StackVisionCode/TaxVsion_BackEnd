using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Contracts.Files;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.Identity;
using BuildingBlocks.Web.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Tasks.Api.Filters;
using TaxVision.Tasks.Application.Files;
using TaxVision.Tasks.ClientForms.Application.Common.Abstractions;
using TaxVision.Tasks.ClientForms.Application.Requests.Files;
using Wolverine;

namespace TaxVision.Tasks.Api.Controllers;

/// <summary>
/// Visibilidad por archivo para la biblioteca de CloudStorage, con el token de quien mira: devuelve solo los archivos
/// que esa persona vería en el drawer de la tarea o del form request. Lo que no ve se omite, sin error.
/// </summary>
[ApiController]
[Route("tasks/files")]
[AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin)]
public sealed class TaskFilesController(
    IMessageBus bus,
    IUserPermissionsSource permissions,
    IClientFormsAvailabilityEvaluator clientForms
) : ControllerBase
{
    private static readonly Error InvalidRequest = new(
        "Task.Files.InvalidVisibilityRequest",
        $"fileIds must hold between 1 and {FileVisibilityLimits.Batch} ids and purpose must be list or download."
    );

    [HttpPost("visibility")]
    [HasAnyPermission(TasksPermissions.Read, TasksPermissions.FormRequestsRead)]
    [RateLimit("task.f.file_visibility")]
    [ProducesResponseType<FileVisibilityResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Visibility([FromBody] FileVisibilityRequest? request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var actorId))
            return Unauthorized();
        if (!TryRead(request, out var fileIds, out var purpose))
            return StatusCode(StatusCodes.Status400BadRequest, InvalidRequest);

        var canViewAll = await HasAsync(CustomersPermissions.ViewAll, ct);
        IReadOnlyList<VisibleFile> tasks = await HasAsync(TasksPermissions.Read, ct)
            ? await bus.InvokeAsync<IReadOnlyList<VisibleFile>>(
                new GetVisibleTaskFilesQuery(tenantId, actorId, canViewAll, fileIds),
                ct
            )
            : [];
        // Misma compuerta que la pantalla de formularios: permiso y módulo de la oficina.
        IReadOnlyList<VisibleFile> forms = await IncludesClientFormsAsync(tenantId, ct)
            ? await bus.InvokeAsync<IReadOnlyList<VisibleFile>>(
                new GetVisibleFormFilesQuery(tenantId, actorId, canViewAll, fileIds, purpose),
                ct
            )
            : [];
        return Ok(new FileVisibilityResponse([.. tasks, .. forms]));
    }

    private static bool TryRead(FileVisibilityRequest? request, out IReadOnlyList<Guid> fileIds, out string purpose)
    {
        fileIds = request?.FileIds?.Distinct().ToList() ?? [];
        purpose = request?.Purpose ?? FileVisibilityPurposes.List;
        return fileIds.Count is > 0 and <= FileVisibilityLimits.Batch
            && request!.FileIds.Count <= FileVisibilityLimits.Batch
            && purpose is FileVisibilityPurposes.List or FileVisibilityPurposes.Download;
    }

    private async Task<bool> IncludesClientFormsAsync(Guid tenantId, CancellationToken ct) =>
        await HasAsync(TasksPermissions.FormRequestsRead, ct)
        && (await clientForms.EvaluateAsync(tenantId, ct)).Enabled;

    private Task<bool> HasAsync(string permission, CancellationToken ct) =>
        permissions.HasPermissionAsync(User, permission, ct);
}
