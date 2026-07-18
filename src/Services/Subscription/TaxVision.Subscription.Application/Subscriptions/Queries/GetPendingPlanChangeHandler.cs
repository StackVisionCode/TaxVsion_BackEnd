using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Domain.Subscriptions;

namespace TaxVision.Subscription.Application.Subscriptions.Queries;

public static class GetPendingPlanChangeHandler
{
    public static async Task<Result<PendingPlanChangeResponse?>> Handle(
        GetPendingPlanChangeQuery query,
        ISubscriptionRepository subscriptions,
        CancellationToken ct
    )
    {
        var subscription = await subscriptions.GetByTenantIdAsync(query.TenantId, ct);
        if (subscription is null)
            return Result.Failure<PendingPlanChangeResponse?>(
                new Error("Subscription.NotFound", "Subscription does not exist.")
            );

        // Prioridad: un upgrade en vuelo (AwaitingPayment) es lo más urgente de mostrar, luego
        // un downgrade agendado. Si no hay nada accionable, mostrar el fallo de upgrade más
        // reciente una vez, para que el front pueda avisar que no se aplicó y haga falta otro
        // método de pago.
        var awaitingPayment = subscription.PlanChangeRequests.FirstOrDefault(r =>
            r.Status == PlanChangeRequestStatus.AwaitingPayment
        );
        if (awaitingPayment is not null)
            return Result.Success<PendingPlanChangeResponse?>(FromUpgrade(awaitingPayment));

        var scheduledDowngrade = subscription.PendingDowngrades.FirstOrDefault(d =>
            d.Status == PendingDowngradeStatus.Scheduled
        );
        if (scheduledDowngrade is not null)
            return Result.Success<PendingPlanChangeResponse?>(FromDowngrade(scheduledDowngrade));

        var lastFailedUpgrade = subscription
            .PlanChangeRequests.Where(r => r.Status == PlanChangeRequestStatus.PaymentFailed)
            .OrderByDescending(r => r.RequestedAtUtc)
            .FirstOrDefault();

        return Result.Success(lastFailedUpgrade is null ? null : FromUpgrade(lastFailedUpgrade));
    }

    private static PendingPlanChangeResponse FromUpgrade(PlanChangeRequest request) =>
        new(
            Kind: "Upgrade",
            request.Id,
            request.FromPlanCode,
            request.ToPlanCode,
            request.ToBillingCycle?.ToString(),
            request.Status.ToString(),
            request.RequestedAtUtc,
            EffectiveAtUtc: null,
            request.ChargeAmountCents,
            request.ChargeCurrency
        );

    private static PendingPlanChangeResponse FromDowngrade(PendingDowngrade pending) =>
        new(
            Kind: "Downgrade",
            pending.Id,
            pending.FromPlanCode,
            pending.ToPlanCode,
            pending.ToBillingCycle?.ToString(),
            pending.Status.ToString(),
            pending.RequestedAtUtc,
            pending.EffectiveAtUtc,
            ChargeAmountCents: null,
            ChargeCurrency: null
        );
}
