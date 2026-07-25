using BuildingBlocks.Results;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Billing.Api.Common;
using TaxVision.Billing.Application.Invoices.CreateInvoiceDraft;
using Wolverine;

namespace TaxVision.Billing.Api.Controllers;

/// <summary>
/// Facturación tenant→taxpayer. El tenant y el actor salen siempre del JWT validado, nunca del
/// payload ni de un query param (corrige el gap del CRM legado). SCAFFOLD B1: solo expone el
/// primer endpoint como placeholder; el catálogo completo (UC-01..UC-21) se implementa en B2+.
/// </summary>
[ApiController]
[Route("billing/invoices")]
[Authorize]
public sealed class InvoicesController(IMessageBus bus) : ControllerBase
{
    public sealed record CreateInvoiceDraftRequest();

    [HttpPost]
    [ProducesResponseType<CreateInvoiceDraftResult>(StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateDraft(
        CreateInvoiceDraftRequest request,
        [FromHeader(Name = "Idempotency-Key")] string idempotencyKey,
        CancellationToken ct
    )
    {
        _ = request;
        if (!User.TryGetTenantId(out var tenantId) || !User.TryGetUserId(out var actorId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<CreateInvoiceDraftResult>>(
            new CreateInvoiceDraftCommand(tenantId, actorId, idempotencyKey),
            ct
        );

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
