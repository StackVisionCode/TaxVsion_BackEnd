using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.Identity;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Subscription.Application.AddOns.Commands.CancelAddOn;
using TaxVision.Subscription.Application.AddOns.Commands.PurchaseAddOn;
using TaxVision.Subscription.Application.AddOns.Commands.StartAddOnCheckout;
using TaxVision.Subscription.Application.AddOns.Queries;
using Wolverine;

namespace TaxVision.Subscription.Api.Controllers;

/// <summary>GetCatalog es público ([AllowAnonymous], el filtro de actor type lo saltea); el resto es staff-only del tenant.</summary>
[ApiController]
[Route("addons")]
[Authorize]
[AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
public sealed class AddOnsController(IMessageBus bus) : ControllerBase
{
    /// <summary>
    /// Catálogo público de add-ons. Fase 4.10 (rate limiting) — D-category exempt: mismo criterio
    /// que <see cref="PlansController.GetPlans"/>.
    /// </summary>
    [HttpGet]
    [AllowAnonymous]
    [ResponseCache(Duration = 300)]
    [RateLimitExempt(
        "Public add-on catalog — no JWT, no pre-existing native limiter; adding new protection is out of scope for a migration phase."
    )]
    [ProducesResponseType<IReadOnlyList<AddOnDefinitionResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCatalog(CancellationToken ct)
    {
        var result = await bus.InvokeAsync<Result<IReadOnlyList<AddOnDefinitionResponse>>>(
            new GetAddOnCatalogQuery(),
            ct
        );

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>Add-ons vigentes del tenant autenticado.</summary>
    [HttpGet("tenant")]
    [RateLimit("subscription.f.addon_read")]
    [ProducesResponseType<IReadOnlyList<AddOnResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTenantAddOns(CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<IReadOnlyList<AddOnResponse>>>(
            new GetTenantAddOnsQuery(tenantId),
            ct
        );

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    public sealed record PurchaseAddOnRequest(string AddOnCode, int Quantity, bool AutoRenew);

    [HttpPost]
    [HasPermission(SubscriptionPermissions.AddOnsManage)]
    [AllowActorTypes(ActorType.TenantAdmin, ActorType.PlatformAdmin)]
    [RateLimit("subscription.g.addon_manage")]
    [ProducesResponseType<Guid>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Purchase(PurchaseAddOnRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<Guid>>(
            new PurchaseAddOnCommand(tenantId, request.AddOnCode, request.Quantity, request.AutoRenew, userId),
            ct
        );

        return result.IsSuccess
            ? StatusCode(StatusCodes.Status201Created, result.Value)
            : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    public sealed record StartAddOnCheckoutRequest(
        string AddOnCode,
        int Quantity,
        bool AutoRenew,
        string PayerEmail,
        string SuccessUrl,
        string CancelUrl,
        string? Provider,
        string? Method
    );

    /// <summary>Compra por checkout hosteado, para el tenant sin método en archivo. El add-on no se activa acá:
    /// lo activa el webhook del pago.</summary>
    [HttpPost("checkout")]
    [HasPermission(SubscriptionPermissions.AddOnsManage)]
    [AllowActorTypes(ActorType.TenantAdmin, ActorType.PlatformAdmin)]
    [AllowSurface(AccessSurface.Account)]
    [RateLimit("subscription.l.addon_purchase")]
    [ProducesResponseType<StartAddOnCheckoutResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> StartCheckout(StartAddOnCheckoutRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<StartAddOnCheckoutResponse>>(
            new StartAddOnCheckoutCommand(
                tenantId,
                request.AddOnCode,
                request.Quantity,
                request.AutoRenew,
                request.PayerEmail,
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

    /// <summary>Al volver del proveedor el Landing pollea acá hasta que el webhook activa el add-on.</summary>
    [HttpGet("checkout/{intentId:guid}")]
    [AllowSurface(AccessSurface.Account)]
    [RateLimit("subscription.f.addon_read")]
    [ProducesResponseType<AddOnCheckoutStatusResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetCheckoutStatus(Guid intentId, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<AddOnCheckoutStatusResponse>>(
            new GetAddOnCheckoutStatusQuery(tenantId, intentId),
            ct
        );

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    public sealed record CancelAddOnRequest(string Reason);

    [HttpPost("{id:guid}/cancel")]
    [HasPermission(SubscriptionPermissions.AddOnsManage)]
    [AllowActorTypes(ActorType.TenantAdmin, ActorType.PlatformAdmin)]
    [RateLimit("subscription.g.addon_manage")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Cancel(Guid id, CancelAddOnRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(new CancelAddOnCommand(tenantId, id, request.Reason, userId), ct);

        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
