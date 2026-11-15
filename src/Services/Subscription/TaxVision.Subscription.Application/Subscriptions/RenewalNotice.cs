using TaxVision.Subscription.Domain.AddOns;
using TaxVision.Subscription.Domain.Seats;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Application.Subscriptions;

/// <summary>
/// Cuándo se avisa la renovación de la suscripción y por cuánto. El anual avisa a 30, 7 y 1 días; el mensual a 7 y 1
/// (un aviso de 30 llegaría recién renovado). El monto es el total que se cobrará (E4): plan más add-ons y asientos
/// activos, cada uno a su precio contratado.
/// </summary>
public static class RenewalNotice
{
    private static readonly int[] MonthlyDays = [7, 1];
    private static readonly int[] YearlyDays = [30, 7, 1];

    /// <summary>Hasta dónde mira el job para encontrar renovaciones por avisar.</summary>
    public static int WindowDays => YearlyDays[0] + 1;

    public static bool ShouldNotify(BillingCycle cycle, int daysUntilRenewal) =>
        Array.IndexOf(cycle == BillingCycle.Yearly ? YearlyDays : MonthlyDays, daysUntilRenewal) >= 0;

    public static long TotalCents(
        long planAmountCents,
        IEnumerable<TenantAddOn> addOns,
        IEnumerable<SubscriptionSeat> seats
    ) =>
        planAmountCents
        + addOns
            .Where(addOn => addOn.Status == AddOnStatus.Active)
            .Sum(addOn => Cents(addOn.UnitPrice) * addOn.Quantity)
        + seats.Where(seat => seat.Status == SeatStatus.Active).Sum(seat => Cents(seat.UnitPrice));

    private static long Cents(Money money) => (long)Math.Round(money.Amount * 100m, MidpointRounding.AwayFromZero);
}
