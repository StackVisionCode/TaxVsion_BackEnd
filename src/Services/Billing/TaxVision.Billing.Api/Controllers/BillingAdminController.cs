using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Billing.Api.Authorization;
using TaxVision.Billing.Application.Invoices.RedeclareFileSources;
using Wolverine;

namespace TaxVision.Billing.Api.Controllers;

/// <summary>Endpoints de la plataforma (PlatformAdmin) para mantenimiento de Billing.</summary>
[ApiController]
[Route("billing/admin")]
[AllowActorTypes(ActorType.PlatformAdmin)]
public sealed class BillingAdminController(IMessageBus bus) : ControllerBase
{
    /// <summary>
    /// Backfill (una vez por oficina): re-declara en CloudStorage el PDF vigente y el pagado de cada factura.
    /// <c>dryRun=true</c> (por defecto) solo cuenta. Idempotente.
    /// </summary>
    [HttpPost("redeclare-file-sources")]
    [HasPermission(InvoicingPermissions.Manage)]
    [RateLimit("billing.g.invoice_manage")]
    [ProducesResponseType<RedeclaredFileSourcesReport>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> RedeclareFileSources(
        [FromQuery] Guid tenantId,
        [FromQuery] bool dryRun = true,
        CancellationToken ct = default
    )
    {
        if (tenantId == Guid.Empty)
            return BadRequest(new { error = "tenantId is required." });

        var result = await bus.InvokeAsync<Result<RedeclaredFileSourcesReport>>(
            new RedeclareFileSourcesCommand(tenantId, dryRun),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
