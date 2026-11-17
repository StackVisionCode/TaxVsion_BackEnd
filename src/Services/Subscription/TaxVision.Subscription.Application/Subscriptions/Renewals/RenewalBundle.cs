using BuildingBlocks.Messaging.PaymentAppIntegrationEvents;
using TaxVision.Subscription.Application.Common;
using TaxVision.Subscription.Domain.AddOns;
using TaxVision.Subscription.Domain.Renewals;
using TaxVision.Subscription.Domain.Seats;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Application.Subscriptions.Renewals;

/// <summary>Lo que suman los asientos y add-ons del paquete: sus líneas (a precio bruto) y el neto a cobrar.</summary>
public sealed record RenewalBundleExtras(
    IReadOnlyList<SaaSChargeLine> Lines,
    long NetCents,
    long DiscountCents,
    string? DiscountSource,
    string? DiscountLabel
);

/// <summary>Asientos suspendidos por un fallo del paquete y si se suspendió algo.</summary>
public sealed record RenewalBundleFailure(IReadOnlyList<Guid> SuspendedSeatIds, bool AnySuspended);

/// <summary>
/// D-D: en el aniversario el plan renueva junto con los asientos y add-ons que vencen ese día, en un solo cobro.
/// Cada extra guarda su renovación con una clave hija de la del plan; el resultado del cobro se aplica a todos.
/// Los add-ons con un cambio de tamaño agendado siguen por su job, que mide el uso antes de cobrar.
/// </summary>
public static class RenewalBundle
{
    public const string SeatTarget = "Seat";
    public const string AddOnTarget = "AddOn";
    public const string PlanTarget = "Plan";

    public static string SeatKey(string planKey, Guid seatId) => $"{planKey}:seat:{seatId:N}";

    public static string AddOnKey(string planKey, Guid addOnId) => $"{planKey}:addon:{addOnId:N}";

    public static bool Includes(TenantSubscription subscription, SubscriptionSeat seat) =>
        RenewsWithExtras(subscription)
        && seat is { Status: SeatStatus.Active, AutoRenew: true }
        && seat.CurrentPeriodEndUtc == subscription.CurrentPeriodEndUtc
        && seat.BillingCycle == subscription.BillingCycle;

    public static bool Includes(TenantSubscription subscription, TenantAddOn addOn) =>
        RenewsWithExtras(subscription)
        && addOn is { Status: AddOnStatus.Active, AutoRenew: true, HasScheduledTierChange: false }
        && addOn.CurrentPeriodEndUtc == subscription.CurrentPeriodEndUtc;

    /// <summary>Programa la renovación de cada extra del paquete hasta el próximo fin del plan y arma sus líneas.</summary>
    public static RenewalBundleExtras Begin(
        TenantSubscription subscription,
        string planKey,
        IEnumerable<SubscriptionSeat> seats,
        IEnumerable<TenantAddOn> addOns,
        IReadOnlyDictionary<string, string> addOnNames,
        DateTime nowUtc
    )
    {
        var newEndUtc = subscription.BillingCycle.CalculateNext(subscription.CurrentPeriodEndUtc);
        var lines = new List<SaaSChargeLine>();
        var charges = new List<DiscountedCharge>();

        foreach (var seat in seats.Where(seat => Includes(subscription, seat)))
        {
            if (seat.BeginRenewal(SeatKey(planKey, seat.Id), newEndUtc, Guid.Empty, nowUtc).IsFailure)
                continue;
            var gross = Cents(seat.UnitPrice.Amount);
            charges.Add(Charge(subscription, DiscountChargeKind.Seats, gross, seat.UnitPrice.Currency));
            lines.Add(new SaaSChargeLine($"{seat.Type} seat", 1, gross, SeatTarget, seat.Id));
        }

        foreach (var addOn in addOns.Where(addOn => Includes(subscription, addOn)))
        {
            if (addOn.BeginRenewal(AddOnKey(planKey, addOn.Id), newEndUtc, Guid.Empty, nowUtc).IsFailure)
                continue;
            // P-1: el add-on cobra precio × cantidad, como dice el aviso de renovación.
            var unit = Cents(addOn.UnitPrice.Amount);
            charges.Add(
                Charge(subscription, DiscountChargeKind.AddOns, unit * addOn.Quantity, addOn.UnitPrice.Currency)
            );
            var name = addOnNames.GetValueOrDefault(addOn.AddOnCode) ?? addOn.AddOnCode;
            lines.Add(new SaaSChargeLine(name, addOn.Quantity, unit, AddOnTarget, addOn.Id));
        }

        var discounted = charges.FirstOrDefault(charge => charge.Source is not null);
        return new RenewalBundleExtras(
            lines,
            charges.Sum(charge => charge.NetCents),
            charges.Sum(charge => charge.DiscountCents ?? 0),
            discounted?.Source,
            discounted?.Label
        );
    }

    /// <summary>Cuántos asientos y add-ons siguen esperando el cobro del paquete.</summary>
    public static (int Seats, int AddOns) Count(
        string planKey,
        IEnumerable<SubscriptionSeat> seats,
        IEnumerable<TenantAddOn> addOns
    ) =>
        (
            seats.Count(seat =>
                Pending(seat.Renewals.Select(r => (r.Id, r.IdempotencyKey, r.Status)), planKey) is not null
            ),
            addOns.Count(addOn =>
                Pending(addOn.Renewals.Select(r => (r.Id, r.IdempotencyKey, r.Status)), planKey) is not null
            )
        );

    /// <summary>El paquete se cobró: cada extra renueva (o vuelve, si estaba vencido). Idempotente.</summary>
    public static void Complete(
        string planKey,
        IEnumerable<SubscriptionSeat> seats,
        IEnumerable<TenantAddOn> addOns,
        string? externalPaymentReference,
        DateTime paidAtUtc
    )
    {
        foreach (var seat in seats)
        {
            if (Pending(seat.Renewals.Select(r => (r.Id, r.IdempotencyKey, r.Status)), planKey) is not { } renewalId)
                continue;
            if (seat.Status == SeatStatus.Active)
                seat.CompleteRenewal(renewalId, externalPaymentReference, Guid.Empty, paidAtUtc);
            else
                seat.CompleteReactivation(renewalId, externalPaymentReference, Guid.Empty, paidAtUtc);
        }

        foreach (var addOn in addOns)
        {
            if (Pending(addOn.Renewals.Select(r => (r.Id, r.IdempotencyKey, r.Status)), planKey) is { } renewalId)
                addOn.CompleteRenewal(renewalId, externalPaymentReference, Guid.Empty, paidAtUtc);
        }
    }

    /// <summary>El cobro del paquete falló: cada extra registra el intento; sin reintentos quedan suspendidos.
    /// No manda un correo por ítem: el del plan cuenta el paquete entero.</summary>
    public static RenewalBundleFailure Fail(
        string planKey,
        IEnumerable<SubscriptionSeat> seats,
        IEnumerable<TenantAddOn> addOns,
        string failureCode,
        string failureReason,
        bool willRetry,
        DateTime? nextRetryAtUtc,
        DateTime nowUtc
    )
    {
        var suspendedSeats = new List<Guid>();
        var anySuspended = false;
        foreach (var seat in seats)
        {
            if (Pending(seat.Renewals.Select(r => (r.Id, r.IdempotencyKey, r.Status)), planKey) is not { } renewalId)
                continue;
            var previous = seat.Status;
            seat.FailRenewal(renewalId, failureCode, failureReason, willRetry, nextRetryAtUtc, Guid.Empty, nowUtc);
            if (seat.Status == SeatStatus.PastDue && previous != SeatStatus.PastDue)
                seat.SuspendAfterRetriesExhausted(Guid.Empty, nowUtc);
            if (seat.Status == SeatStatus.Suspended && previous != SeatStatus.Suspended)
                suspendedSeats.Add(seat.Id);
        }

        foreach (var addOn in addOns)
        {
            if (Pending(addOn.Renewals.Select(r => (r.Id, r.IdempotencyKey, r.Status)), planKey) is not { } renewalId)
                continue;
            var previous = addOn.Status;
            addOn.FailRenewal(renewalId, failureCode, failureReason, willRetry, nextRetryAtUtc, Guid.Empty, nowUtc);
            if (addOn.Status == AddOnStatus.PastDue && previous != AddOnStatus.PastDue)
                addOn.SuspendAfterRetriesExhausted(Guid.Empty, nowUtc);
            anySuspended |= addOn.Status == AddOnStatus.Suspended && previous != AddOnStatus.Suspended;
        }

        return new RenewalBundleFailure(suspendedSeats, anySuspended || suspendedSeats.Count > 0);
    }

    // Con cancelación agendada el plan expira en vez de renovar: sus extras no van en un paquete.
    private static bool RenewsWithExtras(TenantSubscription subscription) =>
        subscription is { Status: SubscriptionStatus.Active, CancelAtPeriodEnd: false }
        && !subscription.HoldsExtraRenewals(subscription.CurrentPeriodEndUtc);

    // La renovación del paquete que sigue abierta (programada o con reintento pendiente).
    private static Guid? Pending(IEnumerable<(Guid Id, string Key, RenewalStatus Status)> renewals, string planKey) =>
        renewals
            .Where(renewal =>
                renewal.Key.StartsWith(planKey + ":", StringComparison.Ordinal)
                && renewal.Status != RenewalStatus.Succeeded
            )
            .Select(renewal => (Guid?)renewal.Id)
            .FirstOrDefault();

    private static DiscountedCharge Charge(
        TenantSubscription subscription,
        DiscountChargeKind kind,
        long grossCents,
        string currency
    ) => DiscountedCharge.For(subscription, kind, grossCents, currency, subscription.CurrentPeriodEndUtc);

    private static long Cents(decimal amount) => (long)Math.Round(amount * 100m, MidpointRounding.AwayFromZero);
}
