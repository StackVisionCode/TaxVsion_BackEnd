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
    /// <summary>
    /// parentFolderId null = raiz. ownerType/ownerId (2026-07-20) son opcionales — solo
    /// tienen efecto para staff interno navegando la raiz de un tenant con varios duenos
    /// mezclados (ej. filtrar "solo lo del cliente X"); el portal de cliente ya estaba y
    /// sigue acotado por su propio scope, sin importar lo que se mande aca.
    /// </summary>
    [HttpGet]
    [Authorize(Policy = CloudStoragePermissions.FileView)]
    [ProducesResponseType<FolderContentsResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Contents(
        [FromQuery] Guid? parentFolderId,
        [FromQuery] OwnerType? ownerType,
        [FromQuery] Guid? ownerId,
        CancellationToken ct
    )
    {
        if (!User.TryGet(out var tenantId, out _, out var scope))
            return Unauthorized();

        var result = await bus.InvokeAsync<FolderContentsResponse>(
            new GetFolderContentsQuery(tenantId, scope, parentFolderId, ownerType, ownerId),
            ct
        );
        return Ok(result);
    }

    /// <summary>
    /// 2026-07-20 — arbol COMPLETO en una sola llamada (sidebar expandible), a diferencia
    /// de Contents que trae un nivel por vez. ownerType/ownerId opcionales, mismo criterio
    /// que Contents; sin ninguno de los dos, staff ve el arbol completo del tenant.
    /// </summary>
    [HttpGet("tree")]
    [Authorize(Policy = CloudStoragePermissions.FileView)]
    [ProducesResponseType<IReadOnlyList<FolderTreeNode>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Tree(
        [FromQuery] OwnerType? ownerType,
        [FromQuery] Guid? ownerId,
        CancellationToken ct
    )
    {
        if (!User.TryGet(out var tenantId, out _, out var scope))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<IReadOnlyList<FolderTreeNode>>>(
            new GetFolderTreeQuery(tenantId, scope, ownerType, ownerId),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    public sealed record CreateFolderRequest(
        Guid? ParentFolderId,
        string? Name,
        OwnerType OwnerType,
        Guid? OwnerId,
        string? Category = null
    );

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
                request.OwnerId,
                request.Category
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

    /// <summary>2026-07-20 — rechaza con 409 (Folder.NotEmpty) si tiene subfolders o archivos directos. Ver DeleteFolderHandler.</summary>
    [HttpDelete("{folderId:guid}")]
    [Authorize(Policy = CloudStoragePermissions.FolderManage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid folderId, CancellationToken ct)
    {
        if (!User.TryGet(out var tenantId, out var actorId, out var scope))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(new DeleteFolderCommand(tenantId, actorId, scope, folderId), ct);
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
