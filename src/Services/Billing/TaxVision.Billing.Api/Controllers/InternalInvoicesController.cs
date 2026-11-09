using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Billing.Application.Invoices.ResyncInvoiceArtifacts;
using Wolverine;

namespace TaxVision.Billing.Api.Controllers;

/// <summary>
/// Reposición M2M (BIL-BB2): vuelve a pedir el enlace de cobro y el PDF de una factura emitida. Cross-tenant:
/// exige un token de servicio emitido para la PlatformTenant. No pasa por el Gateway (no empieza por /billing).
/// </summary>
[ApiController]
[Route("internal/billing/invoices")]
[Authorize(Policy = "ServiceOnly")]
[AllowActorTypes(ActorType.Service)]
public sealed class InternalInvoicesController(IMessageBus bus) : ControllerBase
{
    [HttpPost("{invoiceId:guid}/resync-artifacts")]
    [RateLimitExempt("M2M interno de reposición (actor_type=Service, solo PlatformTenant); nunca en el Gateway.")]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> ResyncArtifacts(
        Guid invoiceId,
        [FromQuery] Guid tenantId,
        CancellationToken ct = default
    )
    {
        if (!User.TryGetTenantId(out var caller) || caller != PlatformTenant.Id)
            return Forbid();
        if (tenantId == Guid.Empty)
            return BadRequest(new { error = "tenantId is required." });

        var result = await bus.InvokeAsync<Result>(new ResyncInvoiceArtifactsCommand(tenantId, invoiceId), ct);
        return result.IsSuccess ? Accepted() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
