using BuildingBlocks.Results;
using BuildingBlocks.Web.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TaxVision.PaymentClient.Application.PaymentLinks.Commands.RedeemPaymentLink;
using TaxVision.PaymentClient.Application.PaymentLinks.Queries;
using Wolverine;

namespace TaxVision.PaymentClient.Api.Public;

/// <summary>
/// Endpoint público sin JWT — el <c>linkToken</c> del path es la única prueba de posesión
/// (§32.2 del diseño). Exento de <c>TenantStatusGateMiddleware</c> (ver
/// <c>ExemptPathPrefixes</c>): el tenant se resuelve del link, no de un JWT que no existe acá.
/// Rate limitado a 1000 req/min/IP (§K.1) — la defensa fina contra prueba de tarjetas es
/// <see cref="TaxVision.PaymentClient.Domain.PaymentLinks.PaymentLink.MarkBlockedAfterExcessiveFailures"/>,
/// esto solo evita un flood bruto.
/// </summary>
[ApiController]
[Route("payments-client/checkout/{linkToken}")]
[AllowAnonymous]
[EnableRateLimiting("public")]
public sealed class HostedCheckoutController(IMessageBus bus) : ControllerBase
{
    [HttpGet]
    [RateLimitExempt(
        "Endpoint público sin JWT (linkToken en el path es la única prueba de posesión) — TieredRateLimitEvaluator "
            + "solo soporta partición por Tenant/User, así que [RateLimit] fallaría abierto acá. La protección real "
            + "la da el limiter nativo [EnableRateLimiting(\"public\")] (1000 req/min/IP, ver doc-comment de la "
            + "clase), que se deja intacto."
    )]
    [ProducesResponseType<PaymentLinkCheckoutResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Get(string linkToken, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<PaymentLinkCheckoutResponse>>(
            new GetPaymentLinkByTokenQuery(linkToken),
            ct
        );

        return result.IsSuccess ? Ok(result.Value) : NotFound(new { result.Error.Code, result.Error.Message });
    }

    public sealed record PayRequest(
        TaxVision.PaymentClient.Domain.ValueObjects.PaymentProviderCode Provider,
        string ProviderPaymentMethodToken,
        string? ReceiptEmail
    );

    /// <summary>El frontend ya tokenizó el método con el SDK del proveedor ELEGIDO (p. ej. Stripe
    /// Elements) — este endpoint solo recibe la referencia opaca resultante + qué proveedor eligió el
    /// taxpayer, nunca datos crudos de tarjeta.</summary>
    [HttpPost("pay")]
    [RateLimitExempt(
        "Endpoint público sin JWT (linkToken en el path es la única prueba de posesión) — TieredRateLimitEvaluator "
            + "solo soporta partición por Tenant/User, así que [RateLimit] fallaría abierto acá. La protección real "
            + "la da el limiter nativo [EnableRateLimiting(\"public\")] (1000 req/min/IP, ver doc-comment de la "
            + "clase), que se deja intacto."
    )]
    [ProducesResponseType<RedeemPaymentLinkResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Pay(string linkToken, PayRequest request, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<RedeemPaymentLinkResponse>>(
            new RedeemPaymentLinkCommand(
                linkToken,
                request.Provider,
                request.ProviderPaymentMethodToken,
                request.ReceiptEmail
            ),
            ct
        );

        if (result.IsSuccess)
            return Ok(result.Value);

        return result.Error.Code == "PaymentLink.NotFound"
            ? NotFound(new { result.Error.Code, result.Error.Message })
            : BadRequest(new { result.Error.Code, result.Error.Message });
    }
}
