using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Common;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.Identity;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.ResourceAuthorization;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using TaxVision.Notes.Api.Requests;
using TaxVision.Notes.Application.Notes;
using TaxVision.Notes.Application.Notes.Abstractions;
using TaxVision.Notes.Application.Notes.Commands;
using TaxVision.Notes.Application.Notes.Queries;
using TaxVision.Notes.Domain.Notes;
using Wolverine;

namespace TaxVision.Notes.Api.Controllers;

/// <summary>
/// 03_Plan_De_Fases.md §Fase 6 — staff (TenantEmployee/TenantAdmin/PlatformAdmin) únicamente; el
/// CustomerPortal tiene su propio controller (<see cref="PortalNotesController"/>), nunca este.
/// TenantId/UserId SIEMPRE del JWT (<c>this.TryGetTenantAndUser</c>, guardrail
/// <see cref="ControllerIdentityExtensions"/>) — nunca del body/query.
///
/// <para>
/// Archive/Restore/Delete están declarados como <c>notes.manage</c> en la Capa 1
/// (<see cref="HasPermissionAttribute"/>) — la tabla del plan dice "notes.manage (o
/// notes.view_all)", pero un actor con SOLO <c>notes.view_all</c> (sin <c>notes.manage</c>) es un
/// caso de borde que la Capa 1 declarativa no puede expresar como OR (ver
/// <see cref="IUserPermissionsSource"/>, un permiso por atributo). El OR real vive en
/// <see cref="NoteVisibilityPolicy.CanManage"/> dentro del handler (Fase 5): PlatformAdmin ya
/// bypassea la Capa 1 completa (README §41.1), así que en la práctica esto solo afecta a un
/// TenantAdmin con <c>notes.view_all</c> pero sin <c>notes.manage</c> — combinación deliberadamente
/// rara de roles. Esa parte NO se resuelve con <c>IsOwnerOrHasManageHandler&lt;Note&gt;</c> (Fase 9):
/// el handler genérico registra UN SOLO "manage permission" de override por tipo de recurso, y acá
/// harían falta dos reglas distintas por acción (Archive/Restore/Delete: autor o
/// <c>notes.view_all</c>; edición de contenido: SOLO autor, sin override) — expresar eso exigiría
/// dos handlers <c>IsOwnerOrHasManageHandler&lt;Note&gt;</c> registrados a la vez, y como el
/// mecanismo de ASP.NET Core no distingue por <c>OperationAuthorizationRequirement</c> dentro del
/// handler, el segundo (con override <c>notes.view_all</c>) pasaría también en los endpoints de
/// contenido, rompiendo la regla "ve-no-edita". Por eso Archive/Restore/Delete se quedan con el
/// chequeo explícito en <see cref="NoteVisibilityPolicy.CanManage"/> (Application, Fase 5/6) — la
/// forma correcta de expresar un override distinto por operación en este mecanismo — y Fase 9 en
/// cambio conecta <see cref="IsOwnerOrHasManageHandler{TResource}"/> (sin permiso de override, ver
/// <see cref="CheckOwnershipAsync"/>) como defensa en profundidad en los endpoints de EDICIÓN de
/// contenido, donde la regla es uniforme y sí encaja: estrictamente el autor.
/// </para>
/// </summary>
[ApiController]
[Route("notes")]
[AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
public sealed class NotesController(
    IMessageBus bus,
    IUserPermissionsSource permissions,
    INoteRepository notes,
    IAuthorizationService authorizationService,
    IOptionsMonitor<ResourceOwnershipOptions> ownershipOptions
) : ControllerBase
{
    private const int DefaultSize = 20;

    /// <summary>
    /// Fase 9 (03_Plan_De_Fases.md) — chequeo de ownership tras flag, compartido por los endpoints
    /// de edición de contenido. Mismo patrón que <c>ShareLinksController</c>/<c>DraftsController</c>
    /// (RBAC Fase 4): si el flag está apagado (default) o la nota ya no existe/no es visible en este
    /// tenant, no bloquea nada acá — el 404/403 real lo sigue devolviendo el handler de Application.
    /// </summary>
    private async Task<IActionResult?> CheckOwnershipAsync(
        Guid tenantId,
        Guid noteId,
        Microsoft.AspNetCore.Authorization.Infrastructure.OperationAuthorizationRequirement operation,
        CancellationToken ct
    )
    {
        if (!ownershipOptions.CurrentValue.Enabled)
            return null;

        var existing = await notes.GetByIdAsync(tenantId, noteId, ct);
        if (existing is null)
            return null;

        var authorized = await authorizationService.AuthorizeAsync(User, existing, operation);
        return authorized.Succeeded ? null : StatusCode(StatusCodes.Status403Forbidden, NoteErrors.Forbidden);
    }

    [HttpPost]
    [HasPermission(NotesPermissions.Manage)]
    [RateLimit("notes.g.create")]
    [ProducesResponseType<NoteResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(CreateNoteRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<NoteResponse>>(
            new CreateNoteCommand(
                tenantId,
                userId,
                request.Html,
                request.TargetType,
                request.TargetId,
                request.Visibility,
                request.ColorKind
            ),
            ct
        );
        return result.IsSuccess
            ? CreatedAtAction(nameof(GetById), new { id = result.Value.Id }, result.Value)
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpGet("mine")]
    [HasPermission(NotesPermissions.Read)]
    [RateLimit("notes.f.list")]
    [ProducesResponseType<PagedResult<NoteResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Mine([FromQuery] int page, [FromQuery] int size, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<PagedResult<NoteResponse>>(
            new ListMyNotesQuery(tenantId, userId, NormalizePage(page), NormalizeSize(size)),
            ct
        );
        return Ok(result);
    }

    [HttpGet("search")]
    [HasPermission(NotesPermissions.Read)]
    [RateLimit("notes.h.search")]
    [ProducesResponseType<PagedResult<NoteResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Search(
        [FromQuery] string q,
        [FromQuery] int page,
        [FromQuery] int size,
        CancellationToken ct
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var hasViewAll = await permissions.HasPermissionAsync(User, NotesPermissions.ViewAll, ct);
        var result = await bus.InvokeAsync<PagedResult<NoteResponse>>(
            new SearchNotesQuery(
                tenantId,
                q ?? string.Empty,
                userId,
                hasViewAll,
                NormalizePage(page),
                NormalizeSize(size)
            ),
            ct
        );
        return Ok(result);
    }

    [HttpGet]
    [HasPermission(NotesPermissions.Read)]
    [RateLimit("notes.f.list")]
    [ProducesResponseType<PagedResult<NoteResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListByReference(
        [FromQuery] NoteTargetType targetType,
        [FromQuery] Guid targetId,
        [FromQuery] int page,
        [FromQuery] int size,
        CancellationToken ct
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var hasViewAll = await permissions.HasPermissionAsync(User, NotesPermissions.ViewAll, ct);
        var result = await bus.InvokeAsync<PagedResult<NoteResponse>>(
            new ListNotesByReferenceQuery(
                tenantId,
                targetType,
                targetId,
                userId,
                hasViewAll,
                NormalizePage(page),
                NormalizeSize(size)
            ),
            ct
        );
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    [HasPermission(NotesPermissions.Read)]
    [RateLimit("notes.f.get")]
    [ProducesResponseType<NoteResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var hasViewAll = await permissions.HasPermissionAsync(User, NotesPermissions.ViewAll, ct);
        var result = await bus.InvokeAsync<Result<NoteResponse>>(
            new GetNoteQuery(tenantId, id, userId, hasViewAll),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPut("{id:guid}/content")]
    [HasPermission(NotesPermissions.Manage)]
    [RateLimit("notes.g.write")]
    [ProducesResponseType<NoteResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> UpdateContent(Guid id, UpdateNoteContentRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var forbidden = await CheckOwnershipAsync(tenantId, id, Operations.Update, ct);
        if (forbidden is not null)
            return forbidden;

        var result = await bus.InvokeAsync<Result<NoteResponse>>(
            new UpdateNoteContentCommand(tenantId, id, userId, request.Html),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPut("{id:guid}/visibility")]
    [HasPermission(NotesPermissions.Manage)]
    [RateLimit("notes.g.write")]
    [ProducesResponseType<NoteResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ChangeVisibility(
        Guid id,
        ChangeNoteVisibilityRequest request,
        CancellationToken ct
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var forbidden = await CheckOwnershipAsync(tenantId, id, Operations.Update, ct);
        if (forbidden is not null)
            return forbidden;

        var result = await bus.InvokeAsync<Result<NoteResponse>>(
            new ChangeNoteVisibilityCommand(tenantId, id, userId, request.Visibility),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPost("{id:guid}/pin")]
    [HasPermission(NotesPermissions.Manage)]
    [RateLimit("notes.g.write")]
    [ProducesResponseType<NoteResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Pin(Guid id, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var forbidden = await CheckOwnershipAsync(tenantId, id, Operations.Update, ct);
        if (forbidden is not null)
            return forbidden;

        var result = await bus.InvokeAsync<Result<NoteResponse>>(new PinNoteCommand(tenantId, id, userId), ct);
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPost("{id:guid}/unpin")]
    [HasPermission(NotesPermissions.Manage)]
    [RateLimit("notes.g.write")]
    [ProducesResponseType<NoteResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Unpin(Guid id, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var forbidden = await CheckOwnershipAsync(tenantId, id, Operations.Update, ct);
        if (forbidden is not null)
            return forbidden;

        var result = await bus.InvokeAsync<Result<NoteResponse>>(new UnpinNoteCommand(tenantId, id, userId), ct);
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPut("{id:guid}/color")]
    [HasPermission(NotesPermissions.Manage)]
    [RateLimit("notes.g.write")]
    [ProducesResponseType<NoteResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SetColor(Guid id, SetNoteColorRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var forbidden = await CheckOwnershipAsync(tenantId, id, Operations.Update, ct);
        if (forbidden is not null)
            return forbidden;

        var result = await bus.InvokeAsync<Result<NoteResponse>>(
            new SetNoteColorCommand(tenantId, id, userId, request.ColorKind),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPost("{id:guid}/archive")]
    [HasPermission(NotesPermissions.Manage)]
    [RateLimit("notes.g.write")]
    [ProducesResponseType<NoteResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Archive(Guid id, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var hasViewAll = await permissions.HasPermissionAsync(User, NotesPermissions.ViewAll, ct);
        var result = await bus.InvokeAsync<Result<NoteResponse>>(
            new ArchiveNoteCommand(tenantId, id, userId, hasViewAll),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPost("{id:guid}/restore")]
    [HasPermission(NotesPermissions.Manage)]
    [RateLimit("notes.g.write")]
    [ProducesResponseType<NoteResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Restore(Guid id, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var hasViewAll = await permissions.HasPermissionAsync(User, NotesPermissions.ViewAll, ct);
        var result = await bus.InvokeAsync<Result<NoteResponse>>(
            new RestoreNoteCommand(tenantId, id, userId, hasViewAll),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpDelete("{id:guid}")]
    [HasPermission(NotesPermissions.Manage)]
    [RateLimit("notes.g.write")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var hasViewAll = await permissions.HasPermissionAsync(User, NotesPermissions.ViewAll, ct);
        var result = await bus.InvokeAsync<Result>(new DeleteNoteCommand(tenantId, id, userId, hasViewAll), ct);
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPost("{id:guid}/attachments")]
    [HasPermission(NotesPermissions.Manage)]
    [RateLimit("notes.g.write")]
    [ProducesResponseType<NoteResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> AttachFile(Guid id, AttachFileToNoteRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var forbidden = await CheckOwnershipAsync(tenantId, id, Operations.Update, ct);
        if (forbidden is not null)
            return forbidden;

        var result = await bus.InvokeAsync<Result<NoteResponse>>(
            new AttachFileToNoteCommand(
                tenantId,
                id,
                userId,
                request.CloudStorageFileId,
                request.DisplayName,
                request.ContentType,
                request.SizeBytes
            ),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpDelete("{id:guid}/attachments/{fileId:guid}")]
    [HasPermission(NotesPermissions.Manage)]
    [RateLimit("notes.g.write")]
    [ProducesResponseType<NoteResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> DetachFile(Guid id, Guid fileId, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var forbidden = await CheckOwnershipAsync(tenantId, id, Operations.Update, ct);
        if (forbidden is not null)
            return forbidden;

        var result = await bus.InvokeAsync<Result<NoteResponse>>(
            new DetachFileFromNoteCommand(tenantId, id, userId, fileId),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    private static int NormalizePage(int page) => page < 1 ? 1 : page;

    private static int NormalizeSize(int size) => size is < 1 or > 100 ? DefaultSize : size;
}
