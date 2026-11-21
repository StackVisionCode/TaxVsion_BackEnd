using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Tasks.Application.Files;
using Wolverine;

namespace TaxVision.Tasks.Api.Controllers;

/// <summary>Reposiciones de plataforma de Tasks. Solo un PlatformAdmin; nunca un usuario de oficina.</summary>
[ApiController]
[AllowActorTypes(ActorType.PlatformAdmin)]
public sealed class TasksAdminController(IMessageBus bus) : ControllerBase
{
    /// <summary>
    /// Re-declara a CloudStorage los archivos de tareas y formularios de una oficina. <c>dryRun=true</c> (por defecto)
    /// solo cuenta. Idempotente.
    /// </summary>
    [HttpPost("tasks/admin/redeclare-file-sources")]
    [RateLimitExempt("Reposición de plataforma (PlatformAdmin), una corrida por oficina.")]
    [ProducesResponseType<RedeclareFileSourcesReport>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RedeclareFileSources(
        [FromQuery] Guid tenantId,
        [FromQuery] bool dryRun = true,
        CancellationToken ct = default
    )
    {
        if (tenantId == Guid.Empty)
            return BadRequest(new Error("Task.Admin.TenantRequired", "tenantId is required."));

        var report = await bus.InvokeAsync<RedeclareFileSourcesReport>(
            new RedeclareFileSourcesCommand(tenantId, dryRun),
            ct
        );
        return Ok(report);
    }
}
