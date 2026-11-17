using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.Identity;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Subscription.Application.AddOns.Commands.StartAddOnCheckout;
using TaxVision.Subscription.Application.AddOns.TierChange;
using Wolverine;

namespace TaxVision.Subscription.Api.Controllers;

/// <summary>
/// Cambiar el tamaño de un add-on de un grupo exclusivo (storage): subir aplica ya con la diferencia prorrateada;
/// bajar queda para la renovación y solo si los archivos caben. Misma autorización que comprar add-ons.
/// </summary>
[ApiController]
[Route("addons/{id:guid}/tier-change")]
[Authorize]
[HasPermission(SubscriptionPermissions.AddOnsManage)]
[AllowActorTypes(ActorType.TenantAdmin, ActorType.PlatformAdmin)]
[AllowSurface(AccessSurface.Account)]
public sealed class AddOnTierChangeController(IMessageBus bus) : ControllerBase
{
    [HttpGet("preview")]
    [RateLimit("subscription.f.subscription_read")]
    [ProducesResponseType<AddOnTierChangePreviewResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Preview(Guid id, [FromQuery] string toCode, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<AddOnTierChangePreviewResponse>>(
            new GetAddOnTierChangePreviewQuery(tenantId, id, toCode ?? string.Empty),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    public sealed record ChangeTierRequest(string ToCode);

    /// <summary>Subida con la tarjeta guardada, o bajada programada para la renovación.</summary>
    [HttpPost]
    [RateLimit("subscription.g.addon_manage")]
    [ProducesResponseType<AddOnTierChangeResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Change(Guid id, ChangeTierRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<AddOnTierChangeResponse>>(
            new ChangeAddOnTierCommand(tenantId, id, request.ToCode ?? string.Empty, userId),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    public sealed record TierUpgradeCheckoutRequest(
        string ToCode,
        string SuccessUrl,
        string CancelUrl,
        string? Provider,
        string? Method
    );

    /// <summary>Subida pagada por checkout. El email del pagador sale del token.</summary>
    [HttpPost("checkout")]
    [RateLimit("subscription.l.addon_purchase")]
    [ProducesResponseType<StartAddOnCheckoutResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Checkout(Guid id, TierUpgradeCheckoutRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();
        if (!User.TryGetEmail(out var payerEmail))
            return CheckoutPayer.MissingEmail();

        var result = await bus.InvokeAsync<Result<StartAddOnCheckoutResponse>>(
            new StartAddOnTierUpgradeCheckoutCommand(
                tenantId,
                id,
                request.ToCode ?? string.Empty,
                payerEmail,
                request.SuccessUrl,
                request.CancelUrl,
                string.IsNullOrWhiteSpace(request.Provider) ? "Stripe" : request.Provider,
                string.IsNullOrWhiteSpace(request.Method) ? "Card" : request.Method,
                userId
            ),
            ct
        );
        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>Cancela la bajada programada.</summary>
    [HttpDelete]
    [RateLimit("subscription.g.addon_manage")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> CancelScheduled(Guid id, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(new CancelAddOnTierChangeCommand(tenantId, id, userId), ct);
        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
