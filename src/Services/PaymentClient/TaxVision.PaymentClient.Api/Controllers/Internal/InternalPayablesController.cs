using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using TaxVision.PaymentClient.Api.Common;
using TaxVision.PaymentClient.Application.Payables.EnsureInvoicePayable;
using Wolverine;

namespace TaxVision.PaymentClient.Api.Controllers.Internal;

/// <summary>
/// API M2M interna: deja que Billing asegure (find-or-create) el ancla ESTABLE de cobro de una
/// factura y obtenga su URL absoluta para embeberla en el PDF. El link con token se crea perezosamente
/// recién cuando el taxpayer abre esa URL (resolver público). El tenant sale del JWT de servicio
/// (audience taxvision-payments, actor_type=Service, scope payments.links.create). PaymentClient es
/// dueño de la URL: la compone acá, Billing solo la guarda.
/// </summary>
[ApiController]
[Route("internal/payables")]
[Authorize(Policy = "CreatePaymentLinksService")]
[AllowActorTypes(ActorType.Service)]
public sealed class InternalPayablesController(IMessageBus bus, IOptions<PaymentClientPublicOptions> publicOptions)
    : ControllerBase
{
    public sealed record EnsureInvoicePayableRequest(long AmountCents, string Currency, string InvoiceId);

    public sealed record EnsureInvoicePayableApiResponse(Guid PayableId, string Reference, string CheckoutUrl);

    [HttpPost("invoices")]
    // M2M ServiceOnly, pero el JWT de servicio SÍ trae TenantId (JwtTokenGenerator.
    // GenerateScopedServiceToken lo setea siempre) — la exención previa asumía que un JWT M2M
    // no tenía identidad para particionar, lo cual es falso. Categoría J (M2M-friendly).
    [RateLimit("payment_client.j.ensure_invoice")]
    [ProducesResponseType<EnsureInvoicePayableApiResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> EnsureInvoice(EnsureInvoicePayableRequest request, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<EnsureInvoicePayableResponse>>(
            new EnsureInvoicePayableCommand(tenantId, request.AmountCents, request.Currency, request.InvoiceId),
            ct
        );

        if (result.IsFailure)
            return StatusCode(result.Error.ToHttpStatusCode(), result.Error);

        var baseUrl = publicOptions.Value.BaseUrl.TrimEnd('/');
        var checkoutUrl = $"{baseUrl}/payments-client/invoices/{result.Value.Reference}";
        return Ok(new EnsureInvoicePayableApiResponse(result.Value.PayableId, result.Value.Reference, checkoutUrl));
    }
}
