using BuildingBlocks.ActorTypeAuthorization;
using BuildingBlocks.Authorization;
using BuildingBlocks.Results;
using BuildingBlocks.Web.Identity;
using BuildingBlocks.Web.Results;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaxVision.Subscription.Application.Subscriptions.Commands.Activate;
using TaxVision.Subscription.Application.Subscriptions.Commands.Cancel;
using TaxVision.Subscription.Application.Subscriptions.Commands.CancelPendingPlanChange;
using TaxVision.Subscription.Application.Subscriptions.Commands.ChangePlan;
using TaxVision.Subscription.Application.Subscriptions.Commands.Reactivate;
using TaxVision.Subscription.Application.Subscriptions.Commands.Renew;
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
    [ProducesResponseType<MySubscriptionResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetMySubscription(CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out _))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<MySubscriptionResponse>>(new GetMySubscriptionQuery(tenantId), ct);

        return result.IsSuccess ? Ok(result.Value) : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    /// <summary><paramref name="BillingCycle"/> es opcional ("Monthly"/"Yearly") — se manda
    /// junto con el plan en el mismo request. Null mantiene el ciclo actual. Un downgrade (o
    /// un cambio sin diferencia de precio) aplica inmediato, sin cargo, sin crédito, sin
    /// reembolso. Un upgrade calcula el prorrateo del período en curso y requiere confirmar el
    /// cobro antes de aplicarse — la respuesta es 202 con un estado a pollear, no 204: el plan
    /// NO cambia en esta misma request.</summary>
    public sealed record ChangePlanRequest(string PlanCode, string? BillingCycle = null);

    public sealed record ChangePlanResponse(string Status, Guid? PlanChangeRequestId);

    [HttpPost("change-plan")]
    [HasPermission(SubscriptionPermissions.PlanChange)]
    [AllowActorTypes(ActorType.TenantAdmin, ActorType.PlatformAdmin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ChangePlanResponse>(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> ChangePlan(ChangePlanRequest request, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result<ChangePlanResult>>(
            new ChangePlanCommand(tenantId, request.PlanCode, request.BillingCycle, userId),
            ct
        );

        if (result.IsFailure)
            return StatusCode(result.Error.ToHttpStatusCode(), result.Error);

        return result.Value.AwaitingPayment
            ? Accepted(new ChangePlanResponse("PaymentProcessing", result.Value.PlanChangeRequestId))
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

    /// <summary>Cambio de plan pendiente (diferido a fin de período), si existe alguno.</summary>
    [HttpGet("plan-change")]
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

    [HttpPost("plan-change/cancel")]
    [HasPermission(SubscriptionPermissions.PlanChange)]
    [AllowActorTypes(ActorType.TenantAdmin, ActorType.PlatformAdmin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> CancelPendingPlanChange(CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out var tenantId, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(new CancelPendingPlanChangeCommand(tenantId, userId), ct);

        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }

    public sealed record CancelRequest(string Reason);

    [HttpPost("cancel")]
    [HasPermission(SubscriptionPermissions.PlanChange)]
    [AllowActorTypes(ActorType.TenantAdmin, ActorType.PlatformAdmin)]
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
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Renew(Guid tenantId, CancellationToken ct)
    {
        if (!this.TryGetTenantAndUser(out _, out var userId))
            return Unauthorized();

        var result = await bus.InvokeAsync<Result>(new RenewTenantSubscriptionCommand(tenantId, userId), ct);

        return result.IsSuccess ? NoContent() : StatusCode(result.Error.ToHttpStatusCode(), result.Error);
    }
}
