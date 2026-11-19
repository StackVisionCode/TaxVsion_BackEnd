using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Contracts.Work;
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
/// Proveedor de títulos en vivo del historial (Work Profile): devuelve solo las notas que quien mira puede leer,
/// con las reglas del feed (privadas del autor o notes.view_all, asignación de clientes). Tenant del JWT.
/// </summary>
[ApiController]
[Route("notes/work")]
[Authorize]
[AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
public sealed class NotesWorkController(IMessageBus bus, IUserPermissionsSource permissions) : ControllerBase
{
    // ---------- POST /notes/work/titles ----------
    [HttpPost("titles")]
    [HasPermission(NotesPermissions.Read)]
    [RateLimit("notes.f.list")]
    [ProducesResponseType<WorkTitlesResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Titles([FromBody] WorkTitlesRequest? request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var ids = ParseTitleRefs(request);
        if (ids.IsFailure)
            return StatusCode(StatusCodes.Status400BadRequest, ids.Error);
        if (ids.Value.Count == 0)
            return Ok(new WorkTitlesResponse([]));

        var titles = await bus.InvokeAsync<WorkTitlesResponse>(
            new GetNoteWorkTitlesQuery(
                tenantId,
                userId,
                await permissions.HasPermissionAsync(User, NotesPermissions.ViewAll, ct),
                await permissions.HasPermissionAsync(User, CustomersPermissions.ViewAll, ct),
                ids.Value
            ),
            ct
        );
        return Ok(titles);
    }

    // Ids de notas sin repetidos; los tipos ajenos se ignoran. Más de WorkLimits.TitleBatch refs → error.
    private static Result<IReadOnlyList<Guid>> ParseTitleRefs(WorkTitlesRequest? request)
    {
        if (request?.Items is not { } refs)
            return Result.Failure<IReadOnlyList<Guid>>(new Error("Notes.Work.Titles", "items is required."));
        if (refs.Count > WorkLimits.TitleBatch)
            return Result.Failure<IReadOnlyList<Guid>>(
                new Error("Notes.Work.Titles", $"items must have at most {WorkLimits.TitleBatch} refs.")
            );

        IReadOnlyList<Guid> ids = refs.Where(item =>
                item is not null && string.Equals(item.ResourceType, WorkResourceTypes.Note, StringComparison.Ordinal)
            )
            .Select(item => item.Id)
            .Distinct()
            .ToList();
        return Result.Success(ids);
    }
}
