using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.CloudStorage.Api.Common;
using TaxVision.CloudStorage.Application.Folders;
using TaxVision.CloudStorage.Domain.Files;
using Wolverine;

namespace TaxVision.CloudStorage.Api.Controllers;

/// <summary>
/// Fase C2 — arbol navegable de carpetas. Listar usa cloudstorage.file.view (misma
/// autorizacion que navegar archivos); crear/renombrar/mover requieren
/// cloudstorage.folder.manage.
/// </summary>
[ApiController]
[Route("storage/folders")]
[Authorize]
public sealed class FoldersController(IMessageBus bus) : ControllerBase
{
    [HttpGet]
    [Authorize(Policy = CloudStoragePermissions.FileView)]
    [ProducesResponseType<FolderContentsResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Contents([FromQuery] Guid? parentFolderId, CancellationToken ct)
    {
        if (!User.TryGet(out var tenantId, out _, out var scope))
            return Unauthorized();

        var result = await bus.InvokeAsync<FolderContentsResponse>(
            new GetFolderContentsQuery(tenantId, scope, parentFolderId),
            ct
        );
        return Ok(result);
    }

    public sealed record CreateFolderRequest(Guid? ParentFolderId, string? Name, OwnerType OwnerType, Guid? OwnerId);

    [HttpPost]
    [Authorize(Policy = CloudStoragePermissions.FolderManage)]
    [ProducesResponseType<FolderResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(CreateFolderRequest request, CancellationToken ct)
    {
        if (!User.TryGet(out var tenantId, out var actorId, out var scope))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<FolderResponse>>(
            new CreateFolderCommand(
                tenantId,
                actorId,
                scope,
                request.ParentFolderId,
                request.Name,
                request.OwnerType,
                request.OwnerId
            ),
            ct
        );
        return result.IsSuccess
            ? CreatedAtAction(nameof(Contents), new { parentFolderId = result.Value.ParentFolderId }, result.Value)
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    public sealed record RenameFolderRequest(string? NewName);

    [HttpPut("{folderId:guid}/rename")]
    [Authorize(Policy = CloudStoragePermissions.FolderManage)]
    [ProducesResponseType<FolderResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Rename(Guid folderId, RenameFolderRequest request, CancellationToken ct)
    {
        if (!User.TryGet(out var tenantId, out var actorId, out var scope))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<FolderResponse>>(
            new RenameFolderCommand(tenantId, actorId, scope, folderId, request.NewName),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    public sealed record MoveFolderRequest(Guid? NewParentFolderId);

    [HttpPut("{folderId:guid}/move")]
    [Authorize(Policy = CloudStoragePermissions.FolderManage)]
    [ProducesResponseType<FolderResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Move(Guid folderId, MoveFolderRequest request, CancellationToken ct)
    {
        if (!User.TryGet(out var tenantId, out var actorId, out var scope))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<FolderResponse>>(
            new MoveFolderCommand(tenantId, actorId, scope, folderId, request.NewParentFolderId),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
