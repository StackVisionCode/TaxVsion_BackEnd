using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.Common;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.Seats;

namespace TaxVision.Subscription.Application.Subscriptions;

/// <summary>Si la oficina entra en el plan al que quiere bajar. Lo comparten la vista previa y la ejecución.</summary>
public sealed record DowngradeCapacity(int SeatsAfterChange, int? Occupied)
{
    /// <summary>Null cuando Auth no respondió: no se sabe, y eso no es lo mismo que "no entra".</summary>
    public bool Fits => Occupied is not { } occupied || occupied <= SeatsAfterChange;
}

public static class DowngradeFit
{
    public const string ExceedsSeatsCode = "Subscription.DowngradeExceedsSeats";

    /// <summary>Asientos que la oficina tendría con el plan destino: los que trae el plan más los comprados,
    /// que son independientes del plan y sobreviven al cambio.</summary>
    public static int SeatsAfterChange(SubscriptionPlanVersion targetVersion, IReadOnlyList<SubscriptionSeat> seats) =>
        PlanVersionEntitlements.GetInt(targetVersion, "seats.max", fallback: 0) + CountLiveSeats(seats);

    /// <summary>
    /// Cuánta gente ocupa cupo, según Auth. Si Auth no responde se sigue adelante: el downgrade se agenda
    /// para el fin del período y se puede deshacer, así que bloquearlo por un corte pasajero cuesta más de
    /// lo que protege.
    /// </summary>
    public static async Task<DowngradeCapacity> MeasureAsync(
        Guid tenantId,
        SubscriptionPlanVersion targetVersion,
        IReadOnlyList<SubscriptionSeat> seats,
        ITenantUserCountClient userCounts,
        CancellationToken ct
    )
    {
        var occupancy = await userCounts.GetAsync(tenantId, ct);
        return new DowngradeCapacity(SeatsAfterChange(targetVersion, seats), occupancy?.Occupied);
    }

    public static Result Ensure(DowngradeCapacity capacity) =>
        capacity.Fits
            ? Result.Success()
            : Result.Failure(
                new Error(
                    ExceedsSeatsCode,
                    $"Your office uses {capacity.Occupied} seats and the new plan allows {capacity.SeatsAfterChange}. Remove users or buy seats first."
                )
            );

    /// <summary>Asientos comprados que siguen dando cupo.</summary>
    private static int CountLiveSeats(IReadOnlyList<SubscriptionSeat> seats)
    {
        var live = 0;
        foreach (var seat in seats)
        {
            if (seat.Status is not (SeatStatus.Cancelled or SeatStatus.Expired or SeatStatus.Released))
                live++;
        }

        return live;
    }
}
