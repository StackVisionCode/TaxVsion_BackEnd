using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Common;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.Identity;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Mvc;
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

    /// <summary>Historial del ledger (dos deltas), más recientes primero.</summary>
    [HttpGet("transactions")]
    [HasPermission(WalletPermissions.View)]
    [RateLimit("wallet.f.list")]
    [ProducesResponseType<PagedResult<LedgerEntryView>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Transactions([FromQuery] int page, [FromQuery] int size, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<PagedResult<LedgerEntryView>>(
            new ListLedgerQuery(tenantId, NormalizePage(page), NormalizeSize(size)),
            ct
        );
        return Ok(result);
    }

    /// <summary>
    /// Body de <c>POST /wallet/top-ups</c>: monto en centavos + datos del checkout hosteado (el front manda a
    /// dónde volver y qué proveedor; nunca viaja una tarjeta — el cliente paga en Stripe/PayPal por redirect).
    /// </summary>
    public sealed record TopUpRequest(
        long AmountCents,
        string? Currency,
        string PayerEmail,
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

        var result = await bus.InvokeAsync<Result<TopUpCheckoutView>>(
            new TopUpWalletCommand(
                tenantId,
                userId,
                request.AmountCents,
                request.Currency ?? "USD",
                request.PayerEmail,
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
