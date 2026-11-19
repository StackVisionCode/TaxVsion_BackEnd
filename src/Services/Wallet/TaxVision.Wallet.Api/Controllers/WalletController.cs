using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Common;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.Identity;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Wallet.Application.Activity;
using TaxVision.Wallet.Application.Activity.Abstractions;
using TaxVision.Wallet.Application.Activity.Queries;
using TaxVision.Wallet.Application.Pricing;
using TaxVision.Wallet.Application.Wallet;
using TaxVision.Wallet.Application.Wallet.Commands;
using TaxVision.Wallet.Application.Wallet.Queries;
using Wolverine;

namespace TaxVision.Wallet.Api.Controllers;

/// <summary>
/// Monedero del tenant (saldo + historial). TenantId SIEMPRE del JWT. F1 = solo lectura (`wallet.view`);
/// la recarga (F2), cotización (F3) y el cobro por PEP (F4) llegan después. El cobro NO tiene endpoint
/// público: se hace por evento (00_Plan §5).
/// </summary>
[ApiController]
[Route("wallet")]
[AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
public sealed class WalletController(IMessageBus bus) : ControllerBase
{
    private const int DefaultSize = 20;

    /// <summary>Saldo del monedero (micros): confirmado, reservado y disponible.</summary>
    [HttpGet]
    [HasPermission(WalletPermissions.View)]
    [RateLimit("wallet.f.get")]
    [ProducesResponseType<WalletView>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<WalletView>>(new GetWalletQuery(tenantId), ct);
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>
    /// Historial del ledger, más recientes primero. <c>kind</c>: all | topups | charges | reserved. <c>from</c>/<c>to</c>:
    /// rango [desde, hasta): instante ISO (hasta no entra) o "yyyy-MM-dd" (día de la oficina; hasta lo incluye).
    /// </summary>
    [HttpGet("transactions")]
    [HasPermission(WalletPermissions.View)]
    [RateLimit("wallet.f.list")]
    [ProducesResponseType<PagedResult<LedgerEntryView>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Transactions(
        [FromQuery] int page,
        [FromQuery] int size,
        [FromQuery] TransactionKind? kind,
        [FromQuery] string? from,
        [FromQuery] string? to,
        CancellationToken ct
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var query = new ListLedgerQuery(
            tenantId,
            NormalizePage(page),
            NormalizeSize(size),
            kind ?? TransactionKind.All,
            from,
            to
        );
        var result = await bus.InvokeAsync<Result<PagedResult<LedgerEntryView>>>(query, ct);
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>Detalle de un movimiento: origen, canales con su tarifa, pasos de la misma referencia y recarga.</summary>
    [HttpGet("transactions/{id:guid}")]
    [HasPermission(WalletPermissions.View)]
    [RateLimit("wallet.f.get")]
    [ProducesResponseType<TransactionDetailView>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Transaction(Guid id, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<TransactionDetailView>>(new GetTransactionQuery(tenantId, id), ct);
        if (result.IsSuccess)
            return Ok(result.Value);
        return result.Error == WalletActivityErrors.TransactionNotFound
            ? NotFound(result.Error)
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>Gasto del rango (por defecto, el mes de la oficina) contra el periodo anterior, por día y por canal.</summary>
    [HttpGet("summary")]
    [HasPermission(WalletPermissions.View)]
    [RateLimit("wallet.f.get")]
    [ProducesResponseType<WalletSummaryView>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Summary([FromQuery] string? from, [FromQuery] string? to, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<WalletSummaryView>>(
            new GetWalletSummaryQuery(tenantId, from, to),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>
    /// Body de <c>POST /wallet/top-ups</c>: monto en centavos + datos del checkout hosteado (el front manda a
    /// dónde volver y qué proveedor; nunca viaja una tarjeta — el cliente paga en Stripe/PayPal por redirect).
    /// </summary>
    public sealed record TopUpRequest(
        long AmountCents,
        string? Currency,
        // Se ignora (el pagador sale del token); se acepta para no romper clientes desplegados.
        string? PayerEmail,
        string SuccessUrl,
        string CancelUrl,
        string? Provider,
        string? Method
    );

    /// <summary>Inicia una recarga por checkout hosteado: crea la orden y devuelve la <c>CheckoutUrl</c> del
    /// proveedor para redirigir. NO acredita — el saldo sube al confirmarse el pago (webhook).</summary>
    [HttpPost("top-ups")]
    [HasPermission(WalletPermissions.Manage)]
    [RateLimit("wallet.g.topup")]
    [ProducesResponseType<TopUpCheckoutView>(StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateTopUp(TopUpRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();
        if (!User.TryGetEmail(out var payerEmail))
            return StatusCode(
                StatusCodes.Status400BadRequest,
                new Error("Checkout.MissingPayerEmail", "The signed-in user has no email to bill.")
            );

        var result = await TopUpCheckoutFlow.RunAsync(
            bus,
            new TopUpCheckoutRequest(
                tenantId,
                userId,
                request.AmountCents,
                request.Currency ?? "USD",
                payerEmail,
                request.SuccessUrl,
                request.CancelUrl,
                request.Provider ?? "Stripe",
                request.Method ?? "Card"
            ),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>Estado de una recarga (pago + acreditación).</summary>
    [HttpGet("top-ups/{id:guid}")]
    [HasPermission(WalletPermissions.View)]
    [RateLimit("wallet.f.get")]
    [ProducesResponseType<TopUpView>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTopUp(Guid id, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<TopUpView>>(new GetTopUpQuery(tenantId, id), ct);
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    // ---------- F3: pricing + estimación ----------

    /// <summary>Tarifas vigentes (catálogo versionado).</summary>
    [HttpGet("rates")]
    [HasPermission(WalletPermissions.View)]
    [RateLimit("wallet.f.get")]
    [ProducesResponseType<RatesView>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRates(CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out _, out _))
            return Unauthorized();
        var result = await bus.InvokeAsync<Result<RatesView>>(new GetRatesQuery(), ct);
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>Costo estimado de un envío (unidades×tarifa) vs saldo — SIN efectos. Para el preview del front/PEP.</summary>
    [HttpPost("estimate")]
    [HasPermission(WalletPermissions.View)]
    [RateLimit("wallet.f.get")]
    [ProducesResponseType<EstimateView>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Estimate(PerChannelUnits units, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();
        var result = await bus.InvokeAsync<Result<EstimateView>>(new EstimateQuery(tenantId, units), ct);
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>Publica una versión nueva del catálogo de precios. Solo PlatformAdmin. Inmutable (crea otra versión).</summary>
    [HttpPost("pricing/publish")]
    [AllowActorTypes(ActorType.PlatformAdmin)]
    [RateLimit("wallet.g.topup")]
    [ProducesResponseType<RatesView>(StatusCodes.Status200OK)]
    public async Task<IActionResult> PublishPricing(PerChannelUnitPrices prices, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out _, out var userId))
            return Unauthorized();
        var result = await bus.InvokeAsync<Result<RatesView>>(new PublishPricesCommand(userId, prices), ct);
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    private static int NormalizePage(int page) => page < 1 ? 1 : page;

    private static int NormalizeSize(int size) => size is < 1 or > 100 ? DefaultSize : size;
}
