using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Contracts.Files;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.Identity;
using BuildingBlocks.Web.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Notes.Application.Notes.Queries;
using Wolverine;

namespace TaxVision.Notes.Api.Controllers;

/// <summary>
/// Visibilidad por archivo para la biblioteca de CloudStorage, con el token de quien mira: devuelve solo los adjuntos
/// que esa persona vería en Notes (privadas, borradas y asignación como el feed). Lo que no ve se omite, sin error.
/// </summary>
[ApiController]
[Route("notes/files")]
[Authorize]
[AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin)]
public sealed class NoteFilesController(IMessageBus bus, IUserPermissionsSource permissions) : ControllerBase
{
    private static readonly Error InvalidRequest = new(
        "Notes.Files.InvalidVisibilityRequest",
        $"fileIds must hold between 1 and {FileVisibilityLimits.Batch} ids and purpose must be list or download."
    );

    // ---------- POST /notes/files/visibility ----------
    [HttpPost("visibility")]
    [HasPermission(NotesPermissions.Read)]
    [RateLimit("notes.f.file_visibility")]
    [ProducesResponseType<FileVisibilityResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Visibility([FromBody] FileVisibilityRequest? request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();
        if (!TryRead(request, out var fileIds))
            return StatusCode(StatusCodes.Status400BadRequest, InvalidRequest);

        // purpose no cambia nada en Notes: abrir un adjunto no deja huella en la nota.
        var files = await bus.InvokeAsync<IReadOnlyList<VisibleFile>>(
            new GetVisibleNoteFilesQuery(
                tenantId,
                userId,
                await permissions.HasPermissionAsync(User, NotesPermissions.ViewAll, ct),
                await permissions.HasPermissionAsync(User, CustomersPermissions.ViewAll, ct),
                fileIds
            ),
            ct
        );
        return Ok(new FileVisibilityResponse(files));
    }

    private static bool TryRead(FileVisibilityRequest? request, out IReadOnlyList<Guid> fileIds)
    {
        fileIds = request?.FileIds?.Distinct().ToList() ?? [];
        var purpose = request?.Purpose ?? FileVisibilityPurposes.List;
        return fileIds.Count > 0
            && request!.FileIds.Count <= FileVisibilityLimits.Batch
            && purpose is FileVisibilityPurposes.List or FileVisibilityPurposes.Download;
    }
}
