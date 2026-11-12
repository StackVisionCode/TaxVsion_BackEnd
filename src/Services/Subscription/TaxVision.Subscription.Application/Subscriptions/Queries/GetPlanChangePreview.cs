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
    string? BlockedReason,
    // Desglose de lo que se cobra ahora (plan y, si cambia el ciclo, cada extra) y el ahorro frente a mensual.
    IReadOnlyList<PlanChangePreviewLine>? Lines = null,
    long SavingsCents = 0,
    string? BlockedMessage = null
);

public sealed record PlanChangePreviewLine(
    string Kind,
    string Description,
    int Quantity,
    long UnitAmountCents,
    long AmountCents
);

public static class GetPlanChangePreviewHandler
{
    public static async Task<Result<PlanChangePreviewResponse>> Handle(
        GetPlanChangePreviewQuery query,
        ISubscriptionRepository subscriptions,
        IPlanRepository plans,
        ISubscriptionSeatRepository seats,
        ITenantUserCountClient userCounts,
        ITenantAddOnRepository addOns,
        IAddOnDefinitionRepository addOnDefinitions,
        ISeatPricingRepository seatPricing,
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
        var capacityBlocked = isDowngrade && !capacity.Fits;
        var quote = await PlanChangeQuote.BuildAsync(
            subscription,
            change,
            addOns,
            addOnDefinitions,
            seats,
            seatPricing,
            ct
        );
        var blocked = capacityBlocked || quote.Blocked is not null;

        return Result.Success(
            new PlanChangePreviewResponse(
                change.Direction.ToString(),
                change.Plan.Code.Value,
                change.Plan.Name,
                change.EffectiveCycle.ToString(),
                quote.Lines.Count > 0 ? quote.TotalCents : change.TargetAmountCents,
                change.Currency,
                change.CurrentAmountCents,
                ChargedNow: change.Direction == PlanChangeDirection.Upgrade,
                EffectiveAtUtc: isDowngrade ? subscription.CurrentPeriodEndUtc : null,
                capacity.SeatsAfterChange,
                capacity.Occupied,
                blocked,
                capacityBlocked ? DowngradeFit.ExceedsSeatsCode : quote.Blocked?.Code,
                [
                    .. quote.Lines.Select(line => new PlanChangePreviewLine(
                        line.Kind.ToString(),
                        line.Description,
                        line.Quantity,
                        line.UnitAmountCents,
                        line.AmountCents
                    )),
                ],
                quote.SavingsCents,
                quote.Blocked?.Message
            )
        );
    }
}
