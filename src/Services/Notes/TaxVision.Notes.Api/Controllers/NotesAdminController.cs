using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Notes.Application.Notes.Abstractions;
using TaxVision.Notes.Application.Notes.Commands;
using Wolverine;

namespace TaxVision.Notes.Api.Controllers;

/// <summary>Reposiciones de plataforma de Notes. Solo un PlatformAdmin; nunca un usuario de oficina.</summary>
[ApiController]
[Authorize]
[AllowActorTypes(ActorType.PlatformAdmin)]
public sealed class NotesAdminController(IMessageBus bus) : ControllerBase
{
    /// <summary>
    /// Re-declara a CloudStorage los adjuntos de las notas de una oficina. <c>dryRun=true</c> (por defecto) solo
    /// cuenta. Idempotente.
    /// </summary>
    [HttpPost("notes/admin/redeclare-file-sources")]
    [RateLimitExempt("Reposición de plataforma (PlatformAdmin), una corrida por oficina.")]
    [ProducesResponseType<RedeclareFileSourcesReport>(StatusCodes.Status200OK)]
    [ProducesResponseType<Error>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RedeclareFileSources(
        [FromQuery] Guid tenantId,
        [FromQuery] bool dryRun = true,
        CancellationToken ct = default
    )
    {
        if (tenantId == Guid.Empty)
            return BadRequest(new Error("Notes.Admin.TenantRequired", "tenantId is required."));

        var report = await bus.InvokeAsync<RedeclareFileSourcesReport>(
            new RedeclareFileSourcesCommand(tenantId, dryRun),
            ct
        );
        return Ok(report);
    }
}
