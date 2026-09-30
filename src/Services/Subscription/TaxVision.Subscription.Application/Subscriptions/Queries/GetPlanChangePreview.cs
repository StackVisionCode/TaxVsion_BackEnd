using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;

namespace TaxVision.Subscription.Application.Subscriptions.Queries;

public sealed record GetPlanChangePreviewQuery(Guid TenantId, string PlanCode, string? BillingCycle);

/// <summary>
/// Qué pasaría si se confirma este cambio, antes de confirmarlo: cuánto se cobra y cuándo, o desde cuándo
/// aplica el plan más barato, y si la oficina entra en el cupo del plan destino.
/// </summary>
public sealed record PlanChangePreviewResponse(
    string Direction,
    string ToPlanCode,
    string ToPlanName,
    string BillingCycle,
    long AmountCents,
    string Currency,
    long CurrentAmountCents,
    bool ChargedNow,
    DateTime? EffectiveAtUtc,
    int SeatsAfterChange,
    int? OccupiedSeats,
    bool Blocked,
    string? BlockedReason
);

public static class GetPlanChangePreviewHandler
{
    public static async Task<Result<PlanChangePreviewResponse>> Handle(
        GetPlanChangePreviewQuery query,
        ISubscriptionRepository subscriptions,
        IPlanRepository plans,
        ISubscriptionSeatRepository seats,
        ITenantUserCountClient userCounts,
        CancellationToken ct
    )
    {
        var subscription = await subscriptions.GetByTenantIdAsync(query.TenantId, ct);
        if (subscription is null)
            return Result.Failure<PlanChangePreviewResponse>(
                new Error("Subscription.NotFound", "Subscription does not exist.")
            );

        var resolved = await PlanChangeResolver.ResolveAsync(
            subscription,
            query.PlanCode,
            query.BillingCycle,
            plans,
            ct
        );
        if (resolved.IsFailure)
            return Result.Failure<PlanChangePreviewResponse>(resolved.Error);

        var change = resolved.Value;
        var isDowngrade = change.Direction == PlanChangeDirection.Downgrade;
        var capacity = await DowngradeFit.MeasureAsync(
            query.TenantId,
            change.PlanVersion,
            await seats.GetByTenantIdAsync(query.TenantId, ct),
            userCounts,
            ct
        );

        // Solo el downgrade puede no entrar: al subir de plan el cupo nunca baja.
        var blocked = isDowngrade && !capacity.Fits;

        return Result.Success(
            new PlanChangePreviewResponse(
                change.Direction.ToString(),
                change.Plan.Code.Value,
                change.Plan.Name,
                change.EffectiveCycle.ToString(),
                change.TargetAmountCents,
                change.Currency,
                change.CurrentAmountCents,
                ChargedNow: change.Direction == PlanChangeDirection.Upgrade,
                EffectiveAtUtc: isDowngrade ? subscription.CurrentPeriodEndUtc : null,
                capacity.SeatsAfterChange,
                capacity.Occupied,
                blocked,
                blocked ? DowngradeFit.ExceedsSeatsCode : null
            )
        );
    }
}
