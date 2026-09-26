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
        ISubscriptionSeatRepository seats,
        ITenantUserCountClient userCounts,
        IPlanChangeCheckoutPaymentClient checkoutClient,
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

        var resolved = await PlanChangeResolver.ResolveAsync(
            subscription,
            command.PlanCode,
            command.BillingCycle,
            plans,
            ct
        );
        if (resolved.IsFailure)
            return Result.Failure<ChangePlanResult>(resolved.Error);

        var change = resolved.Value;
        if (change.Direction == PlanChangeDirection.None)
            return Result.Success(new ChangePlanResult(AwaitingPayment: false, PlanChangeRequestId: null));

        var plan = change.Plan;
        var planVersion = change.PlanVersion;
        var requestedCycle = change.RequestedCycle;
        var targetPrice = (AmountCents: change.TargetAmountCents, Currency: change.Currency);

        var nowUtc = DateTime.UtcNow;
        var previousPlanCode = subscription.PlanCode;
        var isUpgrade = change.Direction == PlanChangeDirection.Upgrade;

        if (isUpgrade)
        {
            var chargeToken = Guid.NewGuid();
            var paymentIdempotencyKey = IdempotencyKeyFactory.PlanChangeCharge(chargeToken);

            var upgradeResult = subscription.RequestUpgrade(
                plan,
                planVersion,
                requestedCycle,
                targetPrice.AmountCents,
                targetPrice.Currency,
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

            // Dos formas de cobrar el MISMO request: por redirect (única opción sin método en archivo) o
            // off-session. Las dos terminan en SubscriptionPlanChangePaymentSucceeded/Failed.
            string? checkoutUrl = null;
            if (command.WantsHostedCheckout)
            {
                var checkout = await checkoutClient.CreateCheckoutAsync(
                    new PlanChangeCheckoutClientRequest(
                        command.TenantId,
                        awaitingPayment.Id,
                        awaitingPayment.ChargeAmountCents,
                        awaitingPayment.ChargeCurrency,
                        command.PayerEmail!,
                        command.SuccessUrl!,
                        command.CancelUrl!,
                        awaitingPayment.PaymentIdempotencyKey,
                        "Stripe",
                        "Card"
                    ),
                    ct
                );
                if (checkout.IsFailure)
                    return Result.Failure<ChangePlanResult>(checkout.Error);

                subscription.AttachUpgradeCheckout(
                    awaitingPayment.Id,
                    checkout.Value.PaymentId,
                    checkout.Value.CheckoutUrl,
                    checkout.Value.ExpiresAtUtc,
                    command.RequestedByUserId,
                    DateTime.UtcNow
                );
                await unitOfWork.SaveChangesAsync(ct);
                checkoutUrl = checkout.Value.CheckoutUrl;
            }
            else
            {
                await bus.PublishAsync(
                    new SubscriptionPlanChangeDueIntegrationEvent
                    {
                        TenantId = command.TenantId,
                        CorrelationId = correlation.CorrelationId,
                        TenantSubscriptionId = subscription.Id,
                        PlanChangeRequestId = awaitingPayment.Id,
                        TargetPlanId = plan.Id,
                        IdempotencyKey = awaitingPayment.PaymentIdempotencyKey,
                        TargetPlanPrice = awaitingPayment.ChargeAmountCents,
                        Currency = awaitingPayment.ChargeCurrency,
                        RequestedByUserId = awaitingPayment.RequestedByUserId,
                    }
                );
            }

            logger.LogInformation(
                "Tenant {TenantId} requested an upgrade to {PlanCode}; full price charge of {AmountCents} {Currency} in flight (requested by {UserId}).",
                command.TenantId,
                plan.Code.Value,
                awaitingPayment.ChargeAmountCents,
                awaitingPayment.ChargeCurrency,
                command.RequestedByUserId
            );
            return Result.Success(new ChangePlanResult(AwaitingPayment: true, awaitingPayment.Id, checkoutUrl));
        }

        // Downgrade (o mismo precio): nunca cobra, nunca prorratea — se agenda para el fin del
        // período actual y sigue disfrutando el plan actual hasta la próxima renovación.
        // Antes hay que ver si la oficina entra: bajar de plan con más gente de la que el plan admite
        // dejaría a esos usuarios fuera al aplicarse.
        var capacity = await DowngradeFit.MeasureAsync(
            command.TenantId,
            planVersion,
            await seats.GetByTenantIdAsync(command.TenantId, ct),
            userCounts,
            ct
        );
        var fits = DowngradeFit.Ensure(capacity);
        if (fits.IsFailure)
            return Result.Failure<ChangePlanResult>(fits.Error);

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
