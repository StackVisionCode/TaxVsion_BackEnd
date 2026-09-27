using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Common;
using TaxVision.Subscription.Domain.Subscriptions;

namespace TaxVision.Subscription.Application.Subscriptions.Queries;

public static class GetMySubscriptionHandler
{
    public static async Task<Result<MySubscriptionResponse>> Handle(
        GetMySubscriptionQuery query,
        ISubscriptionRepository subscriptions,
        IPlanRepository plans,
        CancellationToken ct
    )
    {
        var subscription = await subscriptions.GetByTenantIdAsync(query.TenantId, ct);
        if (subscription is null)
            return Result.Failure<MySubscriptionResponse>(
                new Error("Subscription.NotFound", "Subscription does not exist.")
            );

        var plan = await plans.GetByIdAsync(subscription.PlanId, ct);
        if (plan is null)
            return Result.Failure<MySubscriptionResponse>(new Error("Plan.NotFound", "Plan does not exist."));

        var planVersion = plan.GetPublishedVersion();
        if (planVersion is null)
            return Result.Failure<MySubscriptionResponse>(
                new Error("Plan.NoPublishedVersion", "Plan has no published version.")
            );

        var cyclePrice = PlanPricing.ResolveBaseSubscriptionPrice(planVersion, subscription.BillingCycle);
        var currentCyclePriceUsd = cyclePrice is null ? 0m : cyclePrice.Value.AmountCents / 100m;

        // Último fallo de cobro (para el copy de dunning) — el renewal más reciente que registró FailureCode.
        var lastFailedRenewal = subscription
            .Renewals.Where(renewal => renewal.FailureCode is not null)
            .OrderByDescending(renewal => renewal.FailedAtUtc)
            .FirstOrDefault();

        var lastPaymentFailure = lastFailedRenewal is null
            ? null
            : new LastPaymentFailureDto(
                lastFailedRenewal.FailureCode!,
                lastFailedRenewal.FailureReason ?? string.Empty,
                lastFailedRenewal.RetryCount,
                lastFailedRenewal.NextRetryAtUtc,
                lastFailedRenewal.FailedAtUtc
            );

        // Mismo criterio que TenantSubscriptionAccessConsumer (Auth Fase 2): staff/clientes quedan cortados
        // cuando la suscripción está Suspended o Expired.
        var billingAccessBlocked = subscription.Status is SubscriptionStatus.Suspended or SubscriptionStatus.Expired;

        // A5 (A6.4) — sin billing.view se responde el mismo contrato sin los datos comerciales. Lo que
        // queda es exactamente lo que el banner de ciclo de vida necesita: el estado, si el acceso está
        // cortado, las fechas del lapso y los módulos (que el shell usa para el menú, no son un precio).
        var canViewBilling = query.CanViewBilling;

        return Result.Success(
            new MySubscriptionResponse(
                canViewBilling ? plan.Code.Value : string.Empty,
                canViewBilling ? plan.Name : string.Empty,
                subscription.Status.ToString(),
                canViewBilling ? subscription.BillingCycle.ToString() : string.Empty,
                canViewBilling ? PlanVersionEntitlements.GetMonthlyPriceUsd(planVersion) : 0m,
                canViewBilling ? currentCyclePriceUsd : 0m,
                canViewBilling ? PlanVersionEntitlements.GetInt(planVersion, "seats.max", fallback: 0) : 0,
                canViewBilling
                    ? PlanVersionEntitlements.GetInt(planVersion, "invitations.max_pending", fallback: 0)
                    : 0,
                canViewBilling ? PlanVersionEntitlements.GetLong(planVersion, "storage.max_bytes", fallback: 0) : 0,
                PlanVersionEntitlements.GetEnabledModules(planVersion),
                subscription.TrialEndsAtUtc,
                subscription.CurrentPeriodStartUtc,
                subscription.CurrentPeriodEndUtc,
                subscription.CancelledAtUtc,
                subscription.NextRenewalAtUtc,
                subscription.GracePeriodEndsAtUtc,
                subscription.SuspendedAtUtc,
                subscription.ExpiredAtUtc,
                canViewBilling ? subscription.SuspensionReason : null,
                canViewBilling ? lastPaymentFailure : null,
                billingAccessBlocked
            )
        );
    }
}
