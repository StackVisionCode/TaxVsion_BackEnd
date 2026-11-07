using BuildingBlocks.Results;
using TaxVision.Subscription.Domain.AddOns;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Domain.Seats;

/// <summary>El cargo inicial de un asiento del lote.</summary>
public sealed record SeatCharge(SubscriptionSeat Seat, InitialCharge Charge);

/// <summary>Lo que deja una compra directa de asientos: los asientos activos y sus cargos.</summary>
public sealed record SeatPurchase(List<SubscriptionSeat> Seats, IReadOnlyList<SeatCharge> Charges);

/// <summary>Reglas de la compra de asientos: pedido válido, suscripción, tope del tenant y cargo por asiento.</summary>
public static class SeatPurchasePolicy
{
    private const int MinQuantity = 1;
    private const int MaxQuantity = 500;

    public static Result<SeatType> ParseRequest(int quantity, string? seatType)
    {
        if (quantity is < MinQuantity or > MaxQuantity)
            return Result.Failure<SeatType>(new Error("Seat.InvalidQuantity", "Quantity must be between 1 and 500."));

        return Enum.TryParse<SeatType>(seatType, ignoreCase: true, out var parsed)
            ? Result.Success(parsed)
            : Result.Failure<SeatType>(new Error("Seat.InvalidType", "Unknown seat type."));
    }

    public static Result EnsureSubscriptionAllows(TenantSubscription subscription) =>
        subscription.AcceptsPurchases
            ? Result.Success()
            : Result.Failure(
                new Error(
                    "Subscription.CannotPurchaseSeats",
                    $"Cannot purchase seats while subscription is {subscription.Status}."
                )
            );

    public static Result EnsureWithinLimit(int maxSeats, IEnumerable<SubscriptionSeat> tenantSeats, int quantity)
    {
        var liveSeats = tenantSeats.Count(seat => seat.Status is not (SeatStatus.Cancelled or SeatStatus.Expired));
        return liveSeats + quantity > maxSeats
            ? Result.Failure(
                new Error(
                    "Seat.MaxSeatsExceeded",
                    $"Purchasing {quantity} seat(s) would exceed the tenant's limit of {maxSeats}."
                )
            )
            : Result.Success();
    }

    /// <param name="subscription">Suscripción base. Serializa compras simultáneas por su RowVersion.</param>
    /// <param name="seatType">Tipo de asiento.</param>
    /// <param name="unitPrice">Precio unitario resuelto del catálogo global.</param>
    /// <param name="quantity">Cantidad pedida.</param>
    /// <param name="autoRenew">Si renuevan solos.</param>
    /// <param name="requestedByUserId">Quién compra.</param>
    /// <param name="nowUtc">Instante de la compra.</param>
    /// <param name="requestedSeatId">Id de cada asiento del lote, derivado de la clave del cliente si vino.</param>
    /// <param name="chargeKey">Clave de idempotencia del cargo por asiento e inicio de período.</param>
    public static Result<SeatPurchase> Purchase(
        TenantSubscription subscription,
        SeatType seatType,
        Money unitPrice,
        int quantity,
        bool autoRenew,
        Guid requestedByUserId,
        DateTime nowUtc,
        Func<int, Guid?> requestedSeatId,
        Func<Guid, DateTime, string> chargeKey
    )
    {
        var built = BuildActivatedSeats(
            subscription,
            seatType,
            unitPrice,
            quantity,
            autoRenew,
            requestedByUserId,
            nowUtc,
            requestedSeatId
        );
        if (built.IsFailure)
            return Result.Failure<SeatPurchase>(built.Error);

        var charges = PrepareInitialCharges(built.Value, subscription, requestedByUserId, nowUtc, chargeKey);
        if (charges.IsFailure)
            return Result.Failure<SeatPurchase>(charges.Error);

        // Serializa compras simultáneas del tenant (y el tope de asientos) por la RowVersion de la suscripción.
        var recorded = subscription.RecordPurchase(requestedByUserId, nowUtc);
        if (recorded.IsFailure)
            return Result.Failure<SeatPurchase>(recorded.Error);

        return Result.Success(new SeatPurchase(built.Value, charges.Value));
    }

    // Asiento Standard = capacidad de cupo: se activa optimísticamente al comprar (co-terminado al período
    // de la base, como los add-ons) para que cuente contra el cupo de inmediato; el cobro prorrateado va
    // async. Si el pago falla, dunning lo lleva a PastDue → grace → Suspended. No se asigna 1-a-1.
    private static Result<List<SubscriptionSeat>> BuildActivatedSeats(
        TenantSubscription subscription,
        SeatType seatType,
        Money unitPrice,
        int quantity,
        bool autoRenew,
        Guid requestedByUserId,
        DateTime nowUtc,
        Func<int, Guid?> requestedSeatId
    )
    {
        var newSeats = new List<SubscriptionSeat>(quantity);

        for (var i = 0; i < quantity; i++)
        {
            // Precio unitario resuelto del catálogo GLOBAL (SeatPricing), COPIADO en el asiento:
            // editar el catálogo luego afecta compras nuevas, no las vigentes.
            var seat = SubscriptionSeat
                .Purchase(
                    subscription.TenantId,
                    seatType,
                    SeatSourceType.Plan,
                    subscription.PlanId,
                    unitPrice,
                    subscription.BillingCycle,
                    autoRenew,
                    requestedByUserId,
                    nowUtc,
                    requestedSeatId(i)
                )
                .Value;

            var activated = seat.Activate(nowUtc, subscription.CurrentPeriodEndUtc, requestedByUserId, nowUtc);
            if (activated.IsFailure)
                return Result.Failure<List<SubscriptionSeat>>(activated.Error);

            newSeats.Add(seat);
        }

        return Result.Success(newSeats);
    }

    // Cargo del período parcial inicial, prorrateado por días sobre el período vigente de la base (idéntico
    // para cada asiento del lote). Devuelve lista vacía si no hay nada que cobrar (proración <= 0).
    private static Result<IReadOnlyList<SeatCharge>> PrepareInitialCharges(
        IReadOnlyList<SubscriptionSeat> seats,
        TenantSubscription subscription,
        Guid actorUserId,
        DateTime nowUtc,
        Func<Guid, DateTime, string> chargeKey
    )
    {
        var charges = new List<SeatCharge>(seats.Count);

        foreach (var seat in seats)
        {
            var prorated = ProrationCalculator.InitialPeriod(
                seat.UnitPrice,
                subscription.CurrentPeriodStartUtc,
                subscription.CurrentPeriodEndUtc,
                nowUtc
            );
            if (prorated.IsFailure)
                return Result.Failure<IReadOnlyList<SeatCharge>>(prorated.Error);

            if (prorated.Value.Amount <= 0m)
                continue;

            var key = chargeKey(seat.Id, seat.CurrentPeriodStartUtc!.Value);
            var begun = seat.BeginInitialCharge(key, actorUserId, nowUtc);
            if (begun.IsFailure)
                return Result.Failure<IReadOnlyList<SeatCharge>>(begun.Error);

            charges.Add(new SeatCharge(seat, InitialCharge.From(key, prorated.Value)));
        }

        return Result.Success<IReadOnlyList<SeatCharge>>(charges);
    }
}
