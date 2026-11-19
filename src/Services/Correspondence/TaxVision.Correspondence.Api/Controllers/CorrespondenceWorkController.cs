using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Contracts.Work;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Correspondence.Application.Abstractions;
using TaxVision.Correspondence.Application.Work;
using Wolverine;

namespace TaxVision.Correspondence.Api.Controllers;

/// <summary>
/// Títulos en vivo para el historial del Work Profile: asuntos de correos y hilos que quien mira podría abrir (gate de
/// buzón + cliente asignado). Lo que no se ve se omite. TenantId siempre del JWT.
/// </summary>
[ApiController]
[Route("correspondence/work")]
[AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
public sealed class CorrespondenceWorkController(
    IMessageBus bus,
    IMailboxVisibilityResolver visibility,
    ICustomerAccessResolver customerAccess
) : ControllerBase
{
    private static readonly Error InvalidTitles = new(
        "Correspondence.Work.Titles",
        $"items is required and must have at most {WorkLimits.TitleBatch} entries."
    );

    // ---------- POST /correspondence/work/titles ----------
    [HttpPost("titles")]
    [HasPermission(CorrespondencePermissions.Read)]
    [RateLimit("correspondence.f.thread_read")]
    [ProducesResponseType<WorkTitlesResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Titles([FromBody] WorkTitlesRequest? request, CancellationToken ct)
    {
        if (request?.Items is not { } items || items.Count > WorkLimits.TitleBatch || items.Any(i => i is null))
            return BadRequest(InvalidTitles);
        if (!User.TryGetTenantId(out var tenantId))
            return Forbid();

        var visible = await visibility.ResolveVisibleAccountIdsAsync(User, tenantId, ct);
        var restrictTo = await customerAccess.ResolveRestrictedUserIdAsync(User, ct);
        var result = await bus.InvokeAsync<WorkTitlesResponse>(
            new GetWorkTitlesQuery(tenantId, items, visible, restrictTo),
            ct
        );
        return Ok(result);
    }
}
