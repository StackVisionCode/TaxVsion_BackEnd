using BuildingBlocks.Common;
using BuildingBlocks.Messaging.SubscriptionIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Common;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.Subscriptions;
using Wolverine;

namespace TaxVision.Subscription.Application.Subscriptions.Commands.ChangePlan;

/// <summary>
/// Sin prorrateo, sin diferencia de precio, sin cálculo por días restantes. La dirección del
/// cambio se decide comparando el precio COMPLETO del plan destino contra el precio COMPLETO
/// del plan actual (mismo criterio simple para cualquier combinación de plan/ciclo):
/// <list type="bullet">
/// <item>Upgrade (destino más caro): cobra el precio completo del plan nuevo antes de
/// aplicarlo — ver <see cref="TenantSubscription.RequestUpgrade"/>.</item>
/// <item>Downgrade (destino igual o más barato): se agenda para el fin del período actual,
/// sin cobrar nada — ver <see cref="TenantSubscription.RequestDowngrade"/>.</item>
/// </list>
/// </summary>
public static class ChangePlanHandler
{
    public static async Task<Result<ChangePlanResult>> Handle(
        ChangePlanCommand command,
        ISubscriptionRepository subscriptions,
        IPlanRepository plans,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        ICorrelationContext correlation,
        ISubscriptionAuditLogWriter audit,
        ILogger<TenantSubscription> logger,
        CancellationToken ct
    )
    {
        var subscription = await subscriptions.GetByTenantIdAsync(command.TenantId, ct);
        if (subscription is null)
            return Result.Failure<ChangePlanResult>(new Error("Subscription.NotFound", "Subscription does not exist."));

        var plan = await plans.GetByCodeAsync(command.PlanCode?.Trim().ToLowerInvariant() ?? string.Empty, ct);
        if (plan is null || plan.Status != PlanStatus.Published)
            return Result.Failure<ChangePlanResult>(new Error("Plan.NotFound", "Plan does not exist."));

        var planVersion = plan.GetPublishedVersion();
        if (planVersion is null)
            return Result.Failure<ChangePlanResult>(
                new Error("Plan.NoPublishedVersion", "Plan has no published version.")
            );

        if (!PlanPricing.TryParseBillingCycle(command.BillingCycle, out var requestedCycle))
            return Result.Failure<ChangePlanResult>(
                new Error("Subscription.InvalidBillingCycle", $"'{command.BillingCycle}' is not a valid billing cycle.")
            );

        var cycleChanged = requestedCycle is not null && requestedCycle.Value != subscription.BillingCycle;
        if (subscription.PlanId == plan.Id && subscription.PlanVersionId == planVersion.Id && !cycleChanged)
            return Result.Success(new ChangePlanResult(AwaitingPayment: false, PlanChangeRequestId: null));

        var effectiveCycle = requestedCycle ?? subscription.BillingCycle;
        var targetPrice = PlanPricing.ResolveBaseSubscriptionPrice(planVersion, effectiveCycle);
        if (targetPrice is null)
            return Result.Failure<ChangePlanResult>(
                new Error(
                    "Plan.NoPriceTier",
                    $"Plan {plan.Code.Value} has no price for billing cycle {effectiveCycle}."
                )
            );

        var currentPlan = await plans.GetByIdAsync(subscription.PlanId, ct);
        var currentPlanVersion = PlanPricing.FindVersion(currentPlan, subscription.PlanVersionId);
        var currentPrice = currentPlanVersion is null
            ? null
            : PlanPricing.ResolveBaseSubscriptionPrice(currentPlanVersion, subscription.BillingCycle);
        if (currentPrice is null)
        {
            return Result.Failure<ChangePlanResult>(
                new Error(
                    "Plan.NoCurrentPriceTier",
                    "Current plan has no resolvable price for its billing cycle; cannot determine upgrade/downgrade direction."
                )
            );
        }

        var nowUtc = DateTime.UtcNow;
        var previousPlanCode = subscription.PlanCode;
        var isUpgrade = targetPrice.Value.AmountCents > currentPrice.Value.AmountCents;

        if (isUpgrade)
        {
            var chargeToken = Guid.NewGuid();
            var paymentIdempotencyKey = IdempotencyKeyFactory.PlanChangeCharge(chargeToken);

            var upgradeResult = subscription.RequestUpgrade(
                plan,
                planVersion,
                requestedCycle,
                targetPrice.Value.AmountCents,
                targetPrice.Value.Currency,
                paymentIdempotencyKey,
                command.RequestedByUserId,
                nowUtc
            );
            if (upgradeResult.IsFailure)
                return Result.Failure<ChangePlanResult>(upgradeResult.Error);

            await unitOfWork.SaveChangesAsync(ct);

            var awaitingPayment = subscription.PlanChangeRequests.First(r =>
                r.Status == PlanChangeRequestStatus.AwaitingPayment
            );

            await AuditEntryFactory.AppendAsync(
                audit,
                command.TenantId,
                "TenantSubscription",
                subscription.Id,
                "TenantSubscription.PlanUpgradeAwaitingPayment",
                command.RequestedByUserId,
                correlation.CorrelationId,
                before: new { PlanCode = previousPlanCode },
                after: new
                {
                    PlanCode = previousPlanCode,
                    PendingPlanCode = plan.Code.Value,
                    awaitingPayment.ChargeAmountCents,
                    awaitingPayment.ChargeCurrency,
                },
                reason: null,
                nowUtc,
                ct
            );

            await bus.PublishAsync(
                new SubscriptionPlanChangeDueIntegrationEvent
                {
                    TenantId = command.TenantId,
                    TenantSubscriptionId = subscription.Id,
                    PlanChangeRequestId = awaitingPayment.Id,
                    TargetPlanId = plan.Id,
                    IdempotencyKey = awaitingPayment.PaymentIdempotencyKey,
                    TargetPlanPrice = awaitingPayment.ChargeAmountCents,
                    Currency = awaitingPayment.ChargeCurrency,
                    RequestedByUserId = awaitingPayment.RequestedByUserId,
                }
            );

            logger.LogInformation(
                "Tenant {TenantId} requested an upgrade to {PlanCode}; full price charge of {AmountCents} {Currency} in flight (requested by {UserId}).",
                command.TenantId,
                plan.Code.Value,
                awaitingPayment.ChargeAmountCents,
                awaitingPayment.ChargeCurrency,
                command.RequestedByUserId
            );
            return Result.Success(new ChangePlanResult(AwaitingPayment: true, awaitingPayment.Id));
        }

        // Downgrade (o mismo precio): nunca cobra, nunca prorratea — se agenda para el fin del
        // período actual y sigue disfrutando el plan actual hasta la próxima renovación.
        var downgradeResult = subscription.RequestDowngrade(
            plan,
            planVersion,
            requestedCycle,
            command.RequestedByUserId,
            nowUtc
        );
        if (downgradeResult.IsFailure)
            return Result.Failure<ChangePlanResult>(downgradeResult.Error);

        await unitOfWork.SaveChangesAsync(ct);

        await AuditEntryFactory.AppendAsync(
            audit,
            command.TenantId,
            "TenantSubscription",
            subscription.Id,
            "TenantSubscription.PlanDowngradeScheduled",
            command.RequestedByUserId,
            correlation.CorrelationId,
            before: new { PlanCode = previousPlanCode },
            after: new
            {
                PlanCode = previousPlanCode,
                PendingPlanCode = plan.Code.Value,
                EffectiveAtUtc = subscription.CurrentPeriodEndUtc,
            },
            reason: null,
            nowUtc,
            ct
        );

        logger.LogInformation(
            "Tenant {TenantId} scheduled a downgrade to {PlanCode}, effective at end of period (requested by {UserId}).",
            command.TenantId,
            plan.Code.Value,
            command.RequestedByUserId
        );
        return Result.Success(new ChangePlanResult(AwaitingPayment: false, PlanChangeRequestId: null));
    }
}
