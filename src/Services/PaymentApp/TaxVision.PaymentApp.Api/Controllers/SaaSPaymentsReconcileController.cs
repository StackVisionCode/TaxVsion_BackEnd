using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.PaymentApp.Api.Common;
using TaxVision.PaymentApp.Application.SaaSPayments.Commands.ReconcileHostedCheckout;
using Wolverine;

namespace TaxVision.PaymentApp.Api.Controllers;

/// <summary>
/// Reconcile al volver del hosted checkout. Vive aparte del <see cref="SaaSPaymentsController"/> para
/// mantener ese controller como superficie read-only (historial + recibos): un fitness verifica que no
/// aparezcan POSTs ahí, y así cualquier movimiento futuro de dinero sobre la superficie del tenant
/// vuelve a saltar.
/// </summary>
[ApiController]
[Route("payments-app/saas-payments")]
[Authorize]
[AllowActorTypes(ActorType.TenantAdmin, ActorType.PlatformAdmin)]
public sealed class SaaSPaymentsReconcileController(IMessageBus bus) : ControllerBase
{
    /// <summary>Al volver del hosted checkout (<c>?checkout=success</c>), el landing llama acá para
    /// capturar/finalizar al instante en vez de esperar al webhook del provider. Clave con PayPal, cuyo
    /// Capture Order es manual y el webhook puede tardar minutos. Idempotente: si el webhook ya cerró
    /// el pago, devuelve el estado terminal sin tocar nada.</summary>
    [HttpPost("{id:guid}/reconcile")]
    [HasPermission(PaymentAppPermissions.SaaSPaymentRead)]
    [AllowActorTypes(ActorType.TenantAdmin, ActorType.PlatformAdmin)]
    [AllowSurface(AccessSurface.Account)]
    [RateLimit("payment_app.f.saas_payment_read")]
    [ProducesResponseType<ReconcileHostedCheckoutResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ReconcileHostedCheckout(Guid id, CancellationToken ct)
    {
        if (!User.TryGetTenantId(out var tenantId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<ReconcileHostedCheckoutResponse>>(
            new ReconcileHostedCheckoutCommand(id, tenantId),
            ct
        );

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
