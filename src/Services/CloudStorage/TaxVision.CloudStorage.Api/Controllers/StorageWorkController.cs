using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Contracts.Work;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.CloudStorage.Api.Common;
using TaxVision.CloudStorage.Application.Files.Queries;
using Wolverine;

namespace TaxVision.CloudStorage.Api.Controllers;

/// <summary>
/// Títulos en vivo para el historial del Work Profile: nombres de archivos que quien mira podría abrir (ABAC de
/// asignación incluido). Lo que no se ve se omite. TenantId siempre del JWT.
/// </summary>
[ApiController]
[Route("storage/work")]
[Authorize]
[AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
public sealed class StorageWorkController(IMessageBus bus, IStorageActorScopeResolver scopeResolver) : ControllerBase
{
    private static readonly Error InvalidTitles = new(
        "CloudStorage.Work.Titles",
        $"items is required and must have at most {WorkLimits.TitleBatch} entries."
    );

    // ---------- POST /storage/work/titles ----------
    [HttpPost("titles")]
    [HasPermission(CloudStoragePermissions.FileView)]
    [RateLimit("cloudstorage.f.file_list")]
    [ProducesResponseType<WorkTitlesResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Titles([FromBody] WorkTitlesRequest? request, CancellationToken ct)
    {
        if (request?.Items is not { } items || items.Count > WorkLimits.TitleBatch || items.Any(i => i is null))
            return BadRequest(InvalidTitles);
        if (!User.TryGet(out var tenantId, out var actorId, out var scope))
            return Unauthorized();

        scope = await scopeResolver.ResolveAsync(User, tenantId, actorId, scope, ct);
        var result = await bus.InvokeAsync<WorkTitlesResponse>(new GetWorkTitlesQuery(tenantId, scope, items), ct);
        return Ok(result);
    }
}
