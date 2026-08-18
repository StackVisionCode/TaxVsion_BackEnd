using BuildingBlocks.Results;
using BuildingBlocks.Web.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using TaxVision.PaymentClient.Api.Common;
using TaxVision.PaymentClient.Application.Payables.ResolvePayable;
using Wolverine;

namespace TaxVision.PaymentClient.Api.Public;

/// <summary>
/// URL ESTABLE de una factura: es la que se embebe en el PDF (vive años). Sin JWT — la referencia
/// opaca del path es la única prueba de posesión. Al abrirse, resuelve al link de checkout vigente
/// (o acuña uno nuevo si el anterior expiró) y redirige (302) al checkout. Así el QR de un PDF viejo
/// nunca queda muerto. Exenta de <c>TenantStatusGateMiddleware</c> (ver ExemptPathPrefixes) y
/// rate-limitada igual que el checkout.
/// </summary>
[ApiController]
[Route("payments-client/invoices/{reference}")]
[AllowAnonymous]
[EnableRateLimiting("public")]
public sealed class PayableResolverController(IMessageBus bus, IOptions<PaymentClientPublicOptions> options)
    : ControllerBase
{
    [HttpGet]
    [RateLimitExempt(
        "Endpoint público sin JWT (reference en el path es la única prueba de posesión) — TieredRateLimitEvaluator "
            + "solo soporta partición por Tenant/User, así que [RateLimit] fallaría abierto acá. La protección real "
            + "la da el limiter nativo [EnableRateLimiting(\"public\")] (ver doc-comment de la clase), que se deja "
            + "intacto."
    )]
    [ProducesResponseType(StatusCodes.Status302Found)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Resolve(string reference, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<ResolvePayableResponse>>(new ResolvePayableCommand(reference), ct);

        if (result.IsFailure)
            return NotFound(new { result.Error.Code, result.Error.Message });

        // 302 a la PÁGINA de checkout del frontend (que consume GET /payments-client/checkout/{token} y
        // renderiza Stripe con la key del tenant). Base configurable (dev = ng serve).
        var pageBase = options.Value.CheckoutPageBaseUrl.TrimEnd('/');
        return Redirect($"{pageBase}/pay/{result.Value.CheckoutToken}");
    }
}
