using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.FinancialInsights.Api.Results;
using TaxVision.FinancialInsights.Application.Imports.Commands;
using Wolverine;

namespace TaxVision.FinancialInsights.Api.Controllers;

/// <summary>Operaciones de plataforma (PlatformAdmin). El tenant va por query.</summary>
[ApiController]
[Authorize]
[Route("financial/admin")]
[AllowActorTypes(ActorType.PlatformAdmin)]
public sealed class FinancialAdminController(IMessageBus bus) : ControllerBase
{
    /// <summary>
    /// Backfill de la biblioteca de archivos: re-declara en CloudStorage el archivo de cada documento de los lotes
    /// del tenant como estado de cuenta. <c>dryRun=true</c> (default) solo cuenta. Idempotente.
    /// </summary>
    [HttpPost("redeclare-file-sources")]
    [HasPermission(FinancialInsightsPermissions.View)]
    [RateLimit("financial.i.import_batch_create")]
    [ProducesResponseType<RedeclaredFileSourcesReport>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> RedeclareFileSources(
        [FromQuery] Guid tenantId,
        [FromQuery] bool dryRun = true,
        CancellationToken ct = default
    )
    {
        var result = await bus.InvokeAsync<Result<RedeclaredFileSourcesReport>>(
            new RedeclareFileSourcesCommand(tenantId, dryRun),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToFinancialHttpStatusCode(), result.Error);
    }
}
