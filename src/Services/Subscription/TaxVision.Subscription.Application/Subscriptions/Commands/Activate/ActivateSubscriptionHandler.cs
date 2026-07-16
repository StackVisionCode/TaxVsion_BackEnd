using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Common;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;
using Wolverine;

namespace TaxVision.Subscription.Application.Subscriptions.Commands.Activate;

/// <summary>
/// Cierra el gap de producto encontrado explorando PaymentApp de punta a punta: un tenant
/// nuevo real se queda en Trialing indefinidamente porque nada convierte el trial a Active
/// con un cobro real salvo que un PlatformAdmin lo fuerce a mano. Este handler es la vía
/// self-service — publica el mismo <see cref="SubscriptionRenewalDueIntegrationEvent"/> que
/// usa el job de renovación periódica, así que PaymentApp no necesita ningún cambio: ya sabe
/// cobrar y reportar éxito/fallo para ese evento.
/// </summary>
public static class ActivateSubscriptionHandler
{
    public static async Task<Result> Handle(
        ActivateSubscriptionCommand command,
        ISubscriptionRepository subscriptions,
        IPlanRepository plans,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ISubscriptionAuditLogWriter audit,
        ILogger<TenantSubscription> logger,
        CancellationToken ct)
    {
        var subscription = await subscriptions.GetByTenantIdAsync(command.TenantId, ct);
        if (subscription is null)
            return Result.Failure(new Error("Subscription.NotFound", "Subscription does not exist."));

        if (subscription.Status != SubscriptionStatus.Trialing)
            return Result.Failure(new Error("Subscription.NotTrialing", "Only a trialing subscription can be activated early."));

        var plan = await plans.GetByIdAsync(subscription.PlanId, ct);
        var planVersion = PlanPricing.FindVersion(plan, subscription.PlanVersionId);
        if (planVersion is null)
            return Result.Failure(new Error("Plan.NoPublishedVersion", "Plan has no matching version."));

        if (!PlanPricing.TryParseBillingCycle(command.BillingCycle, out var requestedCycle))
            return Result.Failure(new Error("Subscription.InvalidBillingCycle", $"'{command.BillingCycle}' is not a valid billing cycle."));

        var effectiveCycle = requestedCycle ?? subscription.BillingCycle;
        if (!planVersion.SupportedBillingCycles.Contains(effectiveCycle))
        {
            return Result.Failure(
                new Error("Subscription.UnsupportedBillingCycle", $"Plan {subscription.PlanCode} does not support billing cycle {effectiveCycle}."));
        }

        var price = PlanPricing.ResolveBaseSubscriptionPrice(planVersion, effectiveCycle);
        if (price is null)
            return Result.Failure(new Error("Plan.NoPriceTier", $"Plan {subscription.PlanCode} has no price for billing cycle {effectiveCycle}."));

        var nowUtc = DateTime.UtcNow;
        var periodEndUtc = effectiveCycle.CalculateNext(nowUtc);

        var convertResult = subscription.ConvertTrialToActive(nowUtc, periodEndUtc, requestedCycle, command.ActorUserId, nowUtc);
        if (convertResult.IsFailure)
            return convertResult;

        var idempotencyKey = IdempotencyKeyFactory.SubscriptionRenewal(subscription.Id, periodEndUtc);
        var chargeResult = subscription.BeginActivationCharge(idempotencyKey, command.ActorUserId, nowUtc);
        if (chargeResult.IsFailure)
            return chargeResult;

        await unitOfWork.SaveChangesAsync(ct);

        await AuditEntryFactory.AppendAsync(
            audit, command.TenantId, "TenantSubscription", subscription.Id, "TenantSubscription.ActivatedEarly",
            command.ActorUserId, correlation.CorrelationId,
            before: new { Status = "Trialing" },
            after: new { Status = "Active", subscription.CurrentPeriodStartUtc, subscription.CurrentPeriodEndUtc },
            reason: null, nowUtc, ct);

        await bus.PublishAsync(new SubscriptionRenewalDueIntegrationEvent
        {
            TenantId = command.TenantId,
            TenantSubscriptionId = subscription.Id,
            PlanCode = subscription.PlanCode,
            PeriodStartUtc = subscription.CurrentPeriodStartUtc,
            PeriodEndUtc = subscription.CurrentPeriodEndUtc,
            IdempotencyKey = idempotencyKey,
            AmountCents = price.Value.AmountCents,
            Currency = price.Value.Currency,
        });

        logger.LogInformation(
            "Tenant {TenantId} activated its subscription early from trial (requested by {UserId}); charge intent published.",
            command.TenantId, command.ActorUserId);

        return Result.Success();
    }
}
