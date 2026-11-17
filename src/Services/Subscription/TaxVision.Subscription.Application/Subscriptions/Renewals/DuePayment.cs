using BuildingBlocks.Messaging.PaymentAppIntegrationEvents;
using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Common;
using TaxVision.Subscription.Domain.AddOns;
using TaxVision.Subscription.Domain.Seats;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Application.Subscriptions.Renewals;

/// <summary>Lo que el cliente eligió pagar. Null en la consulta: todo lo vencido.</summary>
public sealed record DueSelection(IReadOnlyCollection<Guid> SeatIds, IReadOnlyCollection<Guid> AddOnIds);

/// <summary>Una cosa vencida con lo que cuesta ponerla al día y hasta cuándo queda.</summary>
public sealed record DueItem(
    string Kind,
    Guid TargetId,
    string Description,
    long GrossCents,
    long NetCents,
    long DiscountCents,
    DateTime PeriodEndUtc
);

/// <summary>El pago de lo vencido: si va el plan, si es obligatorio, cada ítem y los totales.</summary>
public sealed record DueQuote(
    bool IncludesPlan,
    bool PlanRequired,
    IReadOnlyList<DueItem> Items,
    long TotalNetCents,
    long DiscountCents,
    string Currency,
    string? DiscountSource,
    string? DiscountLabel
)
{
    /// <summary>Una línea por ítem, a precio bruto y con a qué pertenece, para el cobro y el recibo.</summary>
    public IReadOnlyList<SaaSChargeLine> Lines =>
        [.. Items.Select(item => new SaaSChargeLine(item.Description, 1, item.GrossCents, item.Kind, item.TargetId))];
}

/// <summary>
/// "Pay what's due": junta lo vencido (plan, asientos y add-ons suspendidos o vencidos) en un solo pago. Con el plan
/// vencido, va siempre y todo arranca el día del pago y termina junto con él (D-B, P-2). Con el plan al día, cada
/// ítem cobra el tramo hasta el fin del plan a su precio.
/// </summary>
public static class DuePayment
{
    public const string PlanKind = "Plan";
    public const string SeatKind = SubscriptionRenewalIntentItem.SeatKind;
    public const string AddOnKind = SubscriptionRenewalIntentItem.AddOnKind;

    private static readonly SubscriptionStatus[] OverdueStatuses =
    [
        SubscriptionStatus.PastDue,
        SubscriptionStatus.Suspended,
        SubscriptionStatus.Expired,
    ];

    public static bool IsPlanOverdue(TenantSubscription subscription) =>
        Array.IndexOf(OverdueStatuses, subscription.Status) >= 0;

    public static Result<DueQuote> Quote(
        TenantSubscription subscription,
        (long AmountCents, string Currency) planPrice,
        string planName,
        IEnumerable<SubscriptionSeat> seats,
        IEnumerable<TenantAddOn> addOns,
        IReadOnlyDictionary<string, string> addOnNames,
        DueSelection? selection,
        DateTime nowUtc
    )
    {
        var planOverdue = IsPlanOverdue(subscription);
        var endUtc = planOverdue ? subscription.BillingCycle.CalculateNext(nowUtc) : subscription.CurrentPeriodEndUtc;
        var dueSeats = seats.Where(seat => seat.Status is SeatStatus.Suspended or SeatStatus.PastDue).ToList();
        var dueAddOns = addOns.Where(addOn => addOn.Status is AddOnStatus.Suspended or AddOnStatus.PastDue).ToList();

        if (selection is not null)
        {
            if (
                selection.SeatIds.Any(id => dueSeats.All(seat => seat.Id != id))
                || selection.AddOnIds.Any(id => dueAddOns.All(addOn => addOn.Id != id))
            )
                return Result.Failure<DueQuote>(
                    new Error("Subscription.Due.UnknownItem", "One of the chosen items is not due.")
                );

            dueSeats = [.. dueSeats.Where(seat => selection.SeatIds.Contains(seat.Id))];
            dueAddOns = [.. dueAddOns.Where(addOn => selection.AddOnIds.Contains(addOn.Id))];
        }

        var items = new List<DueItem>();
        var charges = new List<DiscountedCharge>();
        if (planOverdue)
            items.Add(
                Item(PlanKind, subscription.Id, $"{planName} plan", planPrice.AmountCents, DiscountChargeKind.Base)
            );

        foreach (var seat in dueSeats)
        {
            var period = SeatAlignment.ForReactivation(seat.UnitPrice, seat.BillingCycle, nowUtc, endUtc);
            items.Add(
                Item(SeatKind, seat.Id, $"{seat.Type} seat", Cents(period.Price), DiscountChargeKind.Seats, period)
            );
        }

        foreach (var addOn in dueAddOns)
        {
            var full = Money.Create(addOn.UnitPrice.Amount * addOn.Quantity, addOn.UnitPrice.Currency).Value;
            var period = SeatAlignment.ForReactivation(full, addOn.BillingCycle, nowUtc, endUtc);
            var name = addOnNames.GetValueOrDefault(addOn.AddOnCode) ?? addOn.AddOnCode;
            var description = addOn.Quantity > 1 ? $"{name} ×{addOn.Quantity}" : name;
            items.Add(Item(AddOnKind, addOn.Id, description, Cents(period.Price), DiscountChargeKind.AddOns, period));
        }

        if (items.Count == 0)
            return Result.Failure<DueQuote>(new Error("Subscription.Due.NothingDue", "There is nothing due to pay."));

        var discounted = charges.FirstOrDefault(charge => charge.Source is not null);
        return Result.Success(
            new DueQuote(
                planOverdue,
                planOverdue,
                items,
                items.Sum(item => item.NetCents),
                items.Sum(item => item.DiscountCents),
                planPrice.Currency,
                discounted?.Source,
                discounted?.Label
            )
        );

        DueItem Item(
            string kind,
            Guid targetId,
            string description,
            long grossCents,
            DiscountChargeKind discountKind,
            SeatPeriodCharge? period = null
        )
        {
            var charge = DiscountedCharge.For(subscription, discountKind, grossCents, planPrice.Currency, nowUtc);
            charges.Add(charge);
            return new DueItem(
                kind,
                targetId,
                description,
                grossCents,
                charge.NetCents,
                charge.DiscountCents ?? 0,
                period?.PeriodEndUtc ?? endUtc
            );
        }
    }

    /// <summary>La intención del checkout con lo cotizado: el plan si va y un ítem por asiento o add-on.</summary>
    public static Result<SubscriptionRenewalIntent> Intent(
        Guid tenantId,
        DueQuote quote,
        BillingCycle billingCycle,
        Guid requestedByUserId,
        DateTime nowUtc
    ) =>
        SubscriptionRenewalIntent.CreateForDue(
            tenantId,
            Money.FromCents(quote.TotalNetCents, quote.Currency),
            billingCycle,
            quote.IncludesPlan,
            [
                .. quote
                    .Items.Where(item => item.Kind != PlanKind)
                    .Select(item => new SubscriptionRenewalIntentItem(item.Kind, item.TargetId, item.NetCents)),
            ],
            requestedByUserId,
            nowUtc
        );

    private static long Cents(Money money) => (long)Math.Round(money.Amount * 100m, MidpointRounding.AwayFromZero);
}

/// <summary>Aplica un pago de lo vencido ya cobrado a los asientos y add-ons que cubre.</summary>
public static class DuePaymentProvisioning
{
    /// <summary>Cada ítem vuelve a Active hasta el fin del plan (o el siguiente, si el tramo era chico) y sus
    /// cobros automáticos pendientes se cancelan. Devuelve esas claves para que PaymentApp no los reintente.
    /// Idempotente: un ítem ya activo no se toca.</summary>
    public static IReadOnlyList<string> Apply(
        SubscriptionRenewalIntent intent,
        TenantSubscription subscription,
        IEnumerable<SubscriptionSeat> seats,
        IEnumerable<TenantAddOn> addOns,
        DateTime paidAtUtc
    )
    {
        var superseded = new List<string>();
        var planEndUtc = subscription.CurrentPeriodEndUtc;
        foreach (var item in intent.Items)
        {
            if (item.Kind == DuePayment.SeatKind && seats.FirstOrDefault(s => s.Id == item.TargetId) is { } seat)
            {
                superseded.AddRange(seat.SettleUnpaidRenewals(intent.RequestedByUserId, paidAtUtc));
                if (seat.Status is SeatStatus.Suspended or SeatStatus.PastDue)
                {
                    var end = SeatAlignment.ForReactivation(seat.UnitPrice, seat.BillingCycle, paidAtUtc, planEndUtc);
                    seat.ReactivateAfterAdminReview(paidAtUtc, end.PeriodEndUtc, intent.RequestedByUserId, paidAtUtc);
                }
            }
            else if (
                item.Kind == DuePayment.AddOnKind
                && addOns.FirstOrDefault(a => a.Id == item.TargetId) is { } addOn
            )
            {
                superseded.AddRange(addOn.SettleUnpaidRenewals(intent.RequestedByUserId, paidAtUtc));
                if (addOn.Status is AddOnStatus.Suspended or AddOnStatus.PastDue)
                {
                    var end = SeatAlignment.ForReactivation(addOn.UnitPrice, addOn.BillingCycle, paidAtUtc, planEndUtc);
                    addOn.ReactivateAfterAdminReview(paidAtUtc, end.PeriodEndUtc, intent.RequestedByUserId, paidAtUtc);
                }
            }
        }

        return superseded;
    }
}
