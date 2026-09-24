using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Common;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.CloudStorage.Api.Common;
using TaxVision.CloudStorage.Application.Abstractions;
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
public sealed class FoldersController(IMessageBus bus, ICorrelationContext correlation) : ControllerBase
{
    /// <summary>Tope de página del listado de carpeta (guardrail anti-abuso).</summary>
    private const int MaxFolderPageSize = 200;

    /// <summary>
    /// parentFolderId null = raiz. ownerType/ownerId son opcionales — solo
    /// tienen efecto para staff interno navegando la raiz de un tenant con varios duenos
    /// mezclados (ej. filtrar "solo lo del cliente X"); el portal de cliente ya estaba y
    /// sigue acotado por su propio scope, sin importar lo que se mande aca.
    /// </summary>
    [HttpGet]
    [HasPermission(CloudStoragePermissions.FileView)]
    [AllowActorTypes(
        ActorType.TenantEmployee,
        ActorType.TenantAdmin,
        ActorType.PlatformAdmin,
        ActorType.CustomerPortal
    )]
    [RateLimit("cloudstorage.f.folder_browse")]
    [ProducesResponseType<FolderContentsResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Contents(
        [FromQuery] Guid? parentFolderId,
        [FromQuery] OwnerType? ownerType,
        [FromQuery] Guid? ownerId,
        [FromQuery] int? skip,
        [FromQuery] int? take,
        [FromQuery] FolderType[]? folderTypes,
        [FromQuery] int[]? taxYears,
        [FromQuery] string[]? extensions,
        [FromQuery] FileStatus[]? statuses,
        [FromQuery] FileSortKey sort = FileSortKey.Name,
        [FromQuery] bool desc = false,
        CancellationToken ct = default
    )
    {
        if (!User.TryGet(out var tenantId, out _, out var scope))
            return Unauthorized();

        // Guardrail: take null = sin paginar (compat); si se pide, se acota a [1, MaxFolderPageSize]
        // para que un cliente no pueda pedir una página gigante. skip nunca negativo.
        int? clampedTake = take is null ? null : Math.Clamp(take.Value, 1, MaxFolderPageSize);
        int clampedSkip = skip is null or < 0 ? 0 : skip.Value;

        var filter = new FolderContentsFilter(
            folderTypes is { Length: > 0 } ? folderTypes : null,
            taxYears is { Length: > 0 } ? taxYears : null,
            extensions is { Length: > 0 } ? extensions : null,
            statuses is { Length: > 0 } ? statuses : null,
            sort,
            desc
        );

        var result = await bus.InvokeAsync<FolderContentsResponse>(
            new GetFolderContentsQuery(
                tenantId,
                scope,
                parentFolderId,
                ownerType,
                ownerId,
                clampedSkip,
                clampedTake,
                filter
            ),
            ct
        );
        return Ok(result);
    }

    /// <summary>
    /// Arbol COMPLETO en una sola llamada (sidebar expandible), a diferencia
    /// de Contents que trae un nivel por vez. ownerType/ownerId opcionales, mismo criterio
    /// que Contents; sin ninguno de los dos, staff ve el arbol completo del tenant.
    /// </summary>
    [HttpGet("tree")]
    [HasPermission(CloudStoragePermissions.FileView)]
    [AllowActorTypes(
        ActorType.TenantEmployee,
        ActorType.TenantAdmin,
        ActorType.PlatformAdmin,
        ActorType.CustomerPortal
    )]
    [RateLimit("cloudstorage.f.folder_browse")]
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
    [HasPermission(CloudStoragePermissions.FolderManage)]
    [AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
    [RateLimit("cloudstorage.g.folder_manage")]
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
    [HasPermission(CloudStoragePermissions.FolderManage)]
    [AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
    [RateLimit("cloudstorage.g.folder_manage")]
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
    [HasPermission(CloudStoragePermissions.FolderManage)]
    [AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
    [RateLimit("cloudstorage.g.folder_manage")]
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

    /// <summary>
    /// Borrado RECURSIVO: manda el contenido (archivos directos y de subcarpetas) a la papelera y
    /// elimina el subárbol. 409 solo si hay archivos en retención legal (Folder.HasLegalHold); 403 si
    /// es carpeta de sistema. Ver DeleteFolderHandler.
    /// </summary>
    [HttpDelete("{folderId:guid}")]
    [HasPermission(CloudStoragePermissions.FolderManage)]
    [AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
    [RateLimit("cloudstorage.g.folder_manage")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid folderId, CancellationToken ct)
    {
        if (!User.TryGet(out var tenantId, out var actorId, out var scope))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(
            new DeleteFolderCommand(tenantId, actorId, scope, folderId, AuditContext()),
            ct
        );
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    private RequestAuditContext AuditContext() =>
        new(
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            Request.Headers.UserAgent.ToString(),
            correlation.CorrelationId
        );
}
