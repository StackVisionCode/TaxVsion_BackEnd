using BuildingBlocks.Results;
using TaxVision.Subscription.Domain.AddOns;
using TaxVision.Subscription.Domain.Seats;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Domain.Subscriptions;

/// <summary>
/// Cambio de ciclo de los extras junto con el plan (decisión C2): cada add-on y asiento activo pasa al ciclo nuevo,
/// a su precio vigente y al mismo período que la base, como Stripe, que no mezcla intervalos en una suscripción.
/// Todo o nada: primero se valida cada extra y solo después se cambia alguno.
/// </summary>
public static class BillingCycleSwitch
{
    public static readonly Error RenewalInProgress = new(
        "Subscription.CycleSwitch.RenewalInProgress",
        "An add-on or seat is finishing a renewal. Try again in a few minutes."
    );

    public static readonly Error ExtraPastDue = new(
        "Subscription.CycleSwitch.ExtraPastDue",
        "An add-on or seat has a pending payment. Settle it before changing the billing cycle."
    );

    public static readonly Error PurchaseInProgress = new(
        "Subscription.CycleSwitch.PurchaseInProgress",
        "A seat purchase is still being processed. Try again in a few minutes."
    );

    public static readonly Error PriceMissing = new(
        "Subscription.CycleSwitch.PriceMissing",
        "An add-on or seat has no price for the new billing cycle."
    );

    /// <summary>Los extras que cambian con el plan: los activos. Los cancelados vencen al fin de su período.</summary>
    public static (IReadOnlyList<TenantAddOn> AddOns, IReadOnlyList<SubscriptionSeat> Seats) IncludedExtras(
        IEnumerable<TenantAddOn> addOns,
        IEnumerable<SubscriptionSeat> seats
    ) => ([.. addOns.Where(a => a.Status == AddOnStatus.Active)], [.. seats.Where(s => s.Status == SeatStatus.Active)]);

    /// <summary>Bloqueos E1 y E3 y compras de asientos en curso, sin cambiar nada. Lo usan la vista previa y el pedido.</summary>
    public static Result EnsureCanSwitch(IEnumerable<TenantAddOn> addOns, IEnumerable<SubscriptionSeat> seats)
    {
        var addOnList = addOns.ToList();
        var seatList = seats.ToList();

        if (addOnList.Any(a => a.Status == AddOnStatus.PastDue) || seatList.Any(s => s.Status == SeatStatus.PastDue))
            return Result.Failure(ExtraPastDue);

        if (seatList.Any(s => s.Status == SeatStatus.Available))
            return Result.Failure(PurchaseInProgress);

        var (includedAddOns, includedSeats) = IncludedExtras(addOnList, seatList);
        if (includedAddOns.Any(a => a.HasRenewalInProgress) || includedSeats.Any(s => s.HasRenewalInProgress))
            return Result.Failure(RenewalInProgress);

        return Result.Success();
    }

    /// <param name="prices">Precio vigente del ciclo nuevo por Id de add-on o asiento.</param>
    public static Result Apply(
        BillingCycle billingCycle,
        DateTime periodStartUtc,
        DateTime periodEndUtc,
        IEnumerable<TenantAddOn> addOns,
        IEnumerable<SubscriptionSeat> seats,
        IReadOnlyDictionary<Guid, Money> prices,
        Guid actorUserId,
        DateTime nowUtc
    )
    {
        var addOnList = addOns.ToList();
        var seatList = seats.ToList();

        var allowed = EnsureCanSwitch(addOnList, seatList);
        if (allowed.IsFailure)
            return allowed;

        var (includedAddOns, includedSeats) = IncludedExtras(addOnList, seatList);
        if (includedAddOns.Any(a => !prices.ContainsKey(a.Id)) || includedSeats.Any(s => !prices.ContainsKey(s.Id)))
            return Result.Failure(PriceMissing);

        foreach (var addOn in includedAddOns)
        {
            var switched = addOn.SwitchCycle(
                billingCycle,
                prices[addOn.Id],
                periodStartUtc,
                periodEndUtc,
                actorUserId,
                nowUtc
            );
            if (switched.IsFailure)
                return switched;
        }

        foreach (var seat in includedSeats)
        {
            var switched = seat.SwitchCycle(
                billingCycle,
                prices[seat.Id],
                periodStartUtc,
                periodEndUtc,
                actorUserId,
                nowUtc
            );
            if (switched.IsFailure)
                return switched;
        }

        return Result.Success();
    }
}
