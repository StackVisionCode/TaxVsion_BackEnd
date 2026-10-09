using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.PaymentApp.Application.Abstractions.Payments;
using TaxVision.PaymentApp.Application.WalletTopUpCheckouts.Commands;
using TaxVision.PaymentApp.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.PaymentApp.Api.Controllers;

/// <summary>M2M-only: el servicio Wallet invoca este endpoint para crear la sesión de checkout hosteada de una
/// recarga (el tenant paga en Stripe/PayPal por redirect; nunca se guarda tarjeta ni se cobra off-session).</summary>
[ApiController]
[Route("internal/wallet")]
[Authorize(Policy = "ServiceOnly")]
[AllowActorTypes(ActorType.Service)]
public sealed class InternalWalletCheckoutController(IMessageBus bus) : ControllerBase
{
    public sealed record CreateWalletTopUpCheckoutRequest(
        Guid TenantId,
        Guid TopUpId,
        long AmountCents,
        string Currency,
        string PayerEmail,
        string SuccessUrl,
        string CancelUrl,
        string IdempotencyKey,
        PaymentProviderCode? Provider = null,
        PaymentMethodKind? Method = null
    );

    [HttpPost("top-ups/checkout")]
    [RateLimitExempt("M2M ServiceOnly — invocado por el servicio Wallet (recarga); nunca expuesto al Gateway público.")]
    [ProducesResponseType<WalletTopUpCheckoutResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> CreateCheckout(CreateWalletTopUpCheckoutRequest request, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<WalletTopUpCheckoutResponse>>(
            new CreateWalletTopUpCheckoutCommand(
                request.TenantId,
                request.TopUpId,
                request.AmountCents,
                request.Currency,
                request.PayerEmail,
                request.SuccessUrl,
                request.CancelUrl,
                request.IdempotencyKey,
                request.Provider ?? PaymentProviderCode.Stripe,
                request.Method ?? PaymentMethodKind.Card
            ),
            ct
        );

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
