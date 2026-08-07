using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Common;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.Identity;
using BuildingBlocks.Web.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Notes.Application.Notes;
using TaxVision.Notes.Application.Notes.Queries;
using TaxVision.Notes.Domain.Notes;
using Wolverine;

namespace TaxVision.Notes.Api.Controllers;

/// <summary>
/// 03_Plan_De_Fases.md §Fase 6 — CustomerPortal, controller separado del staff
/// (<see cref="NotesController"/>): solo lectura de notas <see cref="NoteVisibility.ClientVisible"/>
/// de un target concreto (típicamente su propio Customer). Nunca ve Team/Private/Deleted — el
/// repo (<c>ListClientVisibleAsync</c>, Fase 5) filtra eso directo en SQL, sin pasar por
/// <see cref="NoteVisibilityPolicy"/> (esa regla es solo para el path de staff).
/// </summary>
[ApiController]
[Route("notes/portal")]
[AllowActorTypes(ActorType.CustomerPortal)]
[HasPermission(NotesPermissions.PortalRead)]
public sealed class PortalNotesController(IMessageBus bus) : ControllerBase
{
    private const int DefaultSize = 20;

    [HttpGet]
    [RateLimit("notes.f.portal_read")]
    [ProducesResponseType<PagedResult<NoteResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListClientVisible(
        [FromQuery] NoteTargetType targetType,
        [FromQuery] Guid targetId,
        [FromQuery] int page,
        [FromQuery] int size,
        CancellationToken ct
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<PagedResult<NoteResponse>>(
            new ListClientVisibleNotesQuery(tenantId, targetType, targetId, NormalizePage(page), NormalizeSize(size)),
            ct
        );
        return Ok(result);
    }

    private static int NormalizePage(int page) => page < 1 ? 1 : page;

    private static int NormalizeSize(int size) => size is < 1 or > 100 ? DefaultSize : size;
}
