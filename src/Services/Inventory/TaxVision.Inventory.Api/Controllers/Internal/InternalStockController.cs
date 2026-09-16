using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Results;
using BuildingBlocks.Tenancy;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Inventory.Application.Stock;
using Wolverine;

namespace TaxVision.Inventory.Api.Controllers.Internal;

/// <summary>
/// API M2M interna: Billing descuenta el stock de los productos al EMITIR una factura. Bloqueante — si
/// algún producto rastreado no tiene stock suficiente, no descuenta nada y devuelve 409 para que Billing
/// no emita. Servicios y productos sin rastreo se ignoran. El tenant sale del JWT de servicio (audience
/// amplia TaxVision.Services, actor_type=Service). Idempotente por factura del lado del handler.
/// </summary>
[ApiController]
[Route("internal/stock")]
[Authorize]
[AllowActorTypes(ActorType.Service)]
public sealed class InternalStockController(IMessageBus bus, ITenantContext tenant) : ControllerBase
{
    public sealed record CommitSaleLineRequest(Guid CatalogItemId, int Quantity);

    public sealed record CommitInvoiceSaleRequest(Guid InvoiceId, IReadOnlyList<CommitSaleLineRequest> Lines);

    [HttpPost("commit-sale")]
    [RateLimit("inventory.g.adjust")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CommitSale([FromBody] CommitInvoiceSaleRequest request, CancellationToken ct)
    {
        var lines = (request.Lines ?? []).Select(l => new CommitSaleLine(l.CatalogItemId, l.Quantity)).ToList();

        var result = await bus.InvokeAsync<Result>(
            new CommitInvoiceSaleCommand(tenant.TenantId, request.InvoiceId, lines),
            ct
        );

        return result.IsSuccess ? Ok() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
