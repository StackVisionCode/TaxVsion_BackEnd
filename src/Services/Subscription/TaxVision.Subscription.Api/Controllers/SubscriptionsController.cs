using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.ActorTypeAuthorization;
using BuildingBlocks.Web.Identity;
using BuildingBlocks.Web.RateLimiting;
using BuildingBlocks.Web.Results;
using BuildingBlocks.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Subscription.Application.Subscriptions.Commands.Activate;
using TaxVision.Subscription.Application.Subscriptions.Commands.Cancel;
using TaxVision.Subscription.Application.Subscriptions.Commands.CancelPendingPlanChange;
using TaxVision.Subscription.Application.Subscriptions.Commands.ChangePlan;
using TaxVision.Subscription.Application.Subscriptions.Commands.Reactivate;
using TaxVision.Subscription.Application.Subscriptions.Commands.Renew;
using TaxVision.Subscription.Application.Subscriptions.Commands.Resume;
using TaxVision.Subscription.Application.Subscriptions.Commands.StartRenewalCheckout;
using TaxVision.Subscription.Application.Subscriptions.Commands.Suspend;
using TaxVision.Subscription.Application.Subscriptions.Queries;
using Wolverine;

namespace TaxVision.Subscription.Api.Controllers;

/// <summary>
/// Suscripción del TENANT (la firma contable) a la plataforma TaxVision — nunca un cliente
/// final. Staff-only en todas las acciones (algunas TenantAdmin, otras PlatformAdmin, ambas
/// dentro del set staff); confirmado: cero referencias a customer_id en todo el servicio.
/// </summary>
[ApiController]
[Route("subscriptions")]
[Authorize]
[AllowActorTypes(ActorType.TenantEmployee, ActorType.TenantAdmin, ActorType.PlatformAdmin)]
public sealed class SubscriptionsController(IMessageBus bus) : ControllerBase
{
    /// <summary>Suscripción base del tenant autenticado (plan, límites, renovación, estado).
    /// Los asientos (seats) se consultan por separado — ver /seats.</summary>
    [HttpGet("me")]
    [RateLimit("subscription.f.subscription_read")]
    [ProducesResponseType<MySubscriptionResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMySubscription(CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<MySubscriptionResponse>>(new GetMySubscriptionQuery(tenantId), ct);

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>
    /// Todo lo que el Account (Manage subscription, en el Landing) muestra del lado de Subscription: plan
    /// contratado, período, cambio pendiente, asientos y catálogo de add-ons con su elegibilidad. Los
    /// asientos usados los sabe Auth, así que el front compone con GET /auth/tenants/limits.
    /// </summary>
    [HttpGet("me/account")]
    [HasPermission(SubscriptionPermissions.BillingView)]
    [AllowActorTypes(ActorType.TenantAdmin)]
    [AllowSurface(AccessSurface.Account)]
    [RateLimit("subscription.f.subscription_read")]
    [ProducesResponseType<AccountSubscriptionResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<Error>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> GetAccountSubscription(CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<AccountSubscriptionResponse>>(
            new GetAccountSubscriptionQuery(tenantId),
            ct
        );

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary><paramref name="BillingCycle"/> es opcional ("Monthly"/"Yearly") — se manda
    /// junto con el plan en el mismo request. Null mantiene el ciclo actual. Un downgrade (o
    /// un cambio sin diferencia de precio) aplica inmediato, sin cargo, sin crédito, sin
    /// reembolso. Un upgrade calcula el prorrateo del período en curso y requiere confirmar el
    /// cobro antes de aplicarse — la respuesta es 202 con un estado a pollear, no 204: el plan
    /// NO cambia en esta misma request.</summary>
    /// <summary>Con <c>SuccessUrl</c>/<c>CancelUrl</c> (y el email del pagador) el upgrade se cobra por
    /// checkout hosteado — el único camino para un tenant sin método en archivo. Sin ellas, off-session.</summary>
    public sealed record ChangePlanRequest(
        string PlanCode,
        string? BillingCycle = null,
        string? PayerEmail = null,
        string? SuccessUrl = null,
        string? CancelUrl = null
    );

    public sealed record ChangePlanResponse(string Status, Guid? PlanChangeRequestId, string? CheckoutUrl);

    /// <summary>Qué pasaría con este cambio, antes de confirmarlo: cuánto se cobra y cuándo, o desde cuándo
    /// aplica el plan más barato, y si la oficina entra en el cupo del plan destino.</summary>
    [HttpGet("change-plan/preview")]
    [HasPermission(SubscriptionPermissions.BillingView)]
    [AllowActorTypes(ActorType.TenantAdmin, ActorType.PlatformAdmin)]
    [AllowSurface(AccessSurface.Account)]
    [RateLimit("subscription.f.subscription_read")]
    [ProducesResponseType<PlanChangePreviewResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> PreviewPlanChange(
        [FromQuery] string planCode,
        [FromQuery] string? billingCycle,
        CancellationToken ct
    )
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<PlanChangePreviewResponse>>(
            new GetPlanChangePreviewQuery(tenantId, planCode, billingCycle),
            ct
        );

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPost("change-plan")]
    [HasPermission(SubscriptionPermissions.PlanChange)]
    [AllowActorTypes(ActorType.TenantAdmin, ActorType.PlatformAdmin)]
    [AllowSurface(AccessSurface.Account)]
    [RequireRecentAuthentication]
    [RateLimit("subscription.l.plan_change")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ChangePlanResponse>(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> ChangePlan(ChangePlanRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<ChangePlanResult>>(
            new ChangePlanCommand(
                tenantId,
                request.PlanCode,
                request.BillingCycle,
                userId,
                request.PayerEmail,
                request.SuccessUrl,
                request.CancelUrl
            ),
            ct
        );

        if (result.IsFailure)
            return StatusCode(result.Error.ToHttpStatusCode(), result.Error);

        return result.Value.AwaitingPayment
            ? Accepted(
                new ChangePlanResponse("PaymentProcessing", result.Value.PlanChangeRequestId, result.Value.CheckoutUrl)
            )
            : NoContent();
    }

    /// <summary><paramref name="BillingCycle"/> opcional ("Monthly"/"Yearly") — elegir el
    /// ciclo justo al activar. Null mantiene el que ya tenía el trial (Monthly por defecto).
    /// Self-service: paga ya en vez de esperar a que termine el trial. Solo funciona en
    /// Trialing — dispara un cobro real vía PaymentApp con el precio del ciclo elegido. Si el
    /// cobro falla (sin método de pago, tarjeta rechazada, etc.) la suscripción queda en
    /// PastDue, igual que cualquier renovación fallida.</summary>
    public sealed record ActivateRequest(string? BillingCycle = null);

    [HttpPost("activate")]
    [HasPermission(SubscriptionPermissions.PlanChange)]
    [AllowActorTypes(ActorType.TenantAdmin, ActorType.PlatformAdmin)]
    [RateLimit("subscription.g.subscription_activate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Activate([FromBody] ActivateRequest? request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(
            new ActivateSubscriptionCommand(tenantId, request?.BillingCycle, userId),
            ct
        );

        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>Renovación/reactivación self-service por HOSTED-CHECKOUT (Expiración/Dunning, Fase 4): para el
    /// owner cuya suscripción cayó en lapso (PastDue/GracePeriod/Suspended/Expired) y no tiene método en archivo.
    /// Crea la sesión de checkout y devuelve la URL de redirect; al confirmarse el pago (webhook) la suscripción
    /// vuelve a Active. Solo TenantAdmin/PlatformAdmin — que no quedan bloqueados por billing y pueden entrar a
    /// pagar aunque la oficina esté cortada.</summary>
    public sealed record RenewCheckoutRequest(
        string PayerEmail,
        string SuccessUrl,
        string CancelUrl,
        string? Provider = null,
        string? Method = null
    );

    [HttpPost("me/renew-checkout")]
    [HasPermission(SubscriptionPermissions.PlanChange)]
    [AllowActorTypes(ActorType.TenantAdmin, ActorType.PlatformAdmin)]
    [AllowSurface(AccessSurface.Account)]
    [RateLimit("subscription.l.renew_checkout")]
    [ProducesResponseType<StartRenewalCheckoutResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> StartRenewCheckout(RenewCheckoutRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<StartRenewalCheckoutResponse>>(
            new StartRenewalCheckoutCommand(
                tenantId,
                request.PayerEmail,
                request.SuccessUrl,
                request.CancelUrl,
                request.Provider ?? "Stripe",
                request.Method ?? "Card",
                userId
            ),
            ct
        );

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>Estado de una intención de renovación self-service — el front lo pollea tras volver del checkout
    /// hasta <c>Provisioned</c>/<c>Failed</c>.</summary>
    [HttpGet("me/renew-checkout/{intentId:guid}")]
    [AllowSurface(AccessSurface.Account)]
    [RateLimit("subscription.f.subscription_read")]
    [ProducesResponseType<RenewalCheckoutStatusResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRenewCheckoutStatus(Guid intentId, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<RenewalCheckoutStatusResponse>>(
            new GetRenewalCheckoutStatusQuery(tenantId, intentId),
            ct
        );

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>Cambio de plan pendiente (diferido a fin de período), si existe alguno.</summary>
    [HttpGet("plan-change")]
    [RateLimit("subscription.f.subscription_read")]
    [ProducesResponseType<PendingPlanChangeResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetPendingPlanChange(CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<PendingPlanChangeResponse?>>(
            new GetPendingPlanChangeQuery(tenantId),
            ct
        );

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>Deshacer un downgrade agendado no mueve dinero ni quita nada: no pide step-up.</summary>
    [HttpPost("plan-change/cancel")]
    [HasPermission(SubscriptionPermissions.PlanChange)]
    [AllowActorTypes(ActorType.TenantAdmin, ActorType.PlatformAdmin)]
    [AllowSurface(AccessSurface.Account)]
    [RateLimit("subscription.g.subscription_manage")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> CancelPendingPlanChange(CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(new CancelPendingPlanChangeCommand(tenantId, userId), ct);

        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>Deshace la cancelación programada. No cobra nada ni pide step-up: no quita nada.</summary>
    [HttpPost("resume")]
    [HasPermission(SubscriptionPermissions.PlanChange)]
    [AllowActorTypes(ActorType.TenantAdmin, ActorType.PlatformAdmin)]
    [AllowSurface(AccessSurface.Account)]
    [RateLimit("subscription.g.subscription_manage")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Resume(CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(new ResumeSubscriptionCommand(tenantId, userId), ct);

        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    public sealed record CancelRequest(string Reason);

    /// <summary>Cancelación self-service: NO corta nada ahora. El período ya está pagado, así que el acceso
    /// sigue hasta el fin y ahí expira; sin reembolso (D7). Se deshace con <c>POST subscriptions/resume</c>.</summary>
    [HttpPost("cancel")]
    [HasPermission(SubscriptionPermissions.PlanChange)]
    [AllowActorTypes(ActorType.TenantAdmin, ActorType.PlatformAdmin)]
    [AllowSurface(AccessSurface.Account)]
    [RequireRecentAuthentication]
    [RateLimit("subscription.g.subscription_manage")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Cancel(CancelRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(new CancelSubscriptionCommand(tenantId, request.Reason, userId), ct);

        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    public sealed record SuspendRequest(string Reason);

    /// <summary>Suspensión administrativa (impago, violación de políticas). Solo plataforma.</summary>
    [HttpPatch("{tenantId:guid}/suspend")]
    [HasPermission(SubscriptionPermissions.Suspend)]
    [AllowActorTypes(ActorType.PlatformAdmin)]
    [RateLimit("subscription.g.admin_manage")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Suspend(Guid tenantId, SuspendRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out _, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(
            new SuspendSubscriptionCommand(tenantId, request.Reason, userId),
            ct
        );

        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    [HttpPatch("{tenantId:guid}/reactivate")]
    [HasPermission(SubscriptionPermissions.Reactivate)]
    [AllowActorTypes(ActorType.PlatformAdmin)]
    [RateLimit("subscription.g.admin_manage")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Reactivate(Guid tenantId, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out _, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(new ReactivateSubscriptionCommand(tenantId, userId), ct);

        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary>Renovación manual (mientras no exista Billing). Solo plataforma.</summary>
    [HttpPost("{tenantId:guid}/renew")]
    [HasPermission(SubscriptionPermissions.Renew)]
    [AllowActorTypes(ActorType.PlatformAdmin)]
    [RateLimit("subscription.g.admin_manage")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Renew(Guid tenantId, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out _, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(new RenewTenantSubscriptionCommand(tenantId, userId), ct);

        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
