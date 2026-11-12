using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Domain.AddOns;
using TaxVision.Subscription.Domain.Seats;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Application.Subscriptions;

/// <summary>
/// Lo que cobra un cambio que se paga ya. Si además cambia el ciclo (decisión C2), entran los add-ons y asientos
/// activos al precio vigente del ciclo nuevo, completos y en el mismo pago. Lo comparten la vista previa y el
/// pedido, para que la pantalla no prometa un monto distinto del que se cobra.
/// </summary>
public sealed record PlanChangeQuote(
    IReadOnlyList<PlanChangeLine> Lines,
    long TotalCents,
    long SavingsCents,
    Error? Blocked
)
{
    public const string AddOnUnavailableCode = "Subscription.CycleSwitch.AddOnUnavailable";

    private static readonly PlanChangeQuote Nothing = new([], 0, 0, null);

    /// <summary>Cambia el ciclo y se cobra ya: los extras van en el mismo pago.</summary>
    public static bool IsCycleSwitch(TenantSubscription subscription, PlanChangeResolution change) =>
        change.Direction == PlanChangeDirection.Upgrade
        && change.RequestedCycle is { } cycle
        && cycle != subscription.BillingCycle;

    public static async Task<PlanChangeQuote> BuildAsync(
        TenantSubscription subscription,
        PlanChangeResolution change,
        ITenantAddOnRepository addOns,
        IAddOnDefinitionRepository definitions,
        ISubscriptionSeatRepository seats,
        ISeatPricingRepository seatPricing,
        CancellationToken ct
    )
    {
        if (change.Direction != PlanChangeDirection.Upgrade)
            return Nothing;

        var cycle = change.EffectiveCycle;
        var planLine = PlanChangeLine.ForPlan(
            change.Plan.Code.Value,
            $"{change.Plan.Name} — {CycleLabel(cycle)}",
            change.TargetAmountCents
        );
        if (!IsCycleSwitch(subscription, change))
            return new PlanChangeQuote([planLine], planLine.AmountCents, 0, null);

        var tenantAddOns = await addOns.GetByTenantIdForUpdateAsync(subscription.TenantId, ct);
        var tenantSeats = await seats.GetByTenantIdAsync(subscription.TenantId, ct);

        var allowed = BillingCycleSwitch.EnsureCanSwitch(tenantAddOns, tenantSeats);
        if (allowed.IsFailure)
            return new PlanChangeQuote([], 0, 0, allowed.Error);

        var (includedAddOns, includedSeats) = BillingCycleSwitch.IncludedExtras(tenantAddOns, tenantSeats);
        var lines = new List<PlanChangeLine> { planLine };

        foreach (var addOn in includedAddOns)
        {
            var definition = await definitions.GetByIdAsync(addOn.AddOnDefinitionId, ct);
            var price =
                definition is { Status: AddOnDefinitionStatus.Published }
                && definition.SupportedBillingCycles.Contains(cycle)
                    ? definition.ResolveUnitPrice(cycle, addOn.Quantity)
                    : null;
            if (definition is null || price is null || price.IsFailure)
                return BlockedBy(
                    new Error(
                        AddOnUnavailableCode,
                        $"{definition?.Name ?? addOn.AddOnCode} has no {CycleLabel(cycle)} price. Remove it or keep the current billing cycle."
                    )
                );

            lines.Add(
                PlanChangeLine.ForAddOn(
                    addOn.Id,
                    addOn.AddOnCode,
                    $"{definition.Name} — {CycleLabel(cycle)}",
                    addOn.Quantity,
                    Cents(price.Value)
                )
            );
        }

        if (includedSeats.Count > 0)
        {
            var pricing = await seatPricing.GetAsync(ct);
            foreach (var group in includedSeats.GroupBy(seat => seat.Type).OrderBy(group => group.Key))
            {
                var price = pricing?.ResolveUnitPrice(group.Key, cycle);
                if (price is null || price.IsFailure)
                    return BlockedBy(BillingCycleSwitch.PriceMissing);

                // Un tipo de asiento sin costo cambia de ciclo igual, pero no ensucia el recibo con $0.
                if (Cents(price.Value) > 0)
                    lines.Add(
                        PlanChangeLine.ForSeats(
                            group.Key.ToString(),
                            $"Extra seats ({group.Key}) — {CycleLabel(cycle)}",
                            group.Count(),
                            Cents(price.Value)
                        )
                    );
            }
        }

        var total = lines.Sum(line => line.AmountCents);
        return new PlanChangeQuote(
            lines,
            total,
            Savings(subscription, change, includedAddOns, includedSeats, total),
            null
        );
    }

    /// <summary>Precio por Id de add-on o asiento que el pago confirmado aplica a cada extra.</summary>
    public static IReadOnlyDictionary<Guid, Money> ExtraPrices(
        IReadOnlyCollection<PlanChangeLine> lines,
        IEnumerable<SubscriptionSeat> seats,
        string currency
    )
    {
        var prices = new Dictionary<Guid, Money>();
        foreach (var line in lines.Where(line => line.Kind == PlanChangeLineKind.AddOn && line.ReferenceId is not null))
            prices[line.ReferenceId!.Value] = Money.Create(line.UnitAmountCents / 100m, currency).Value;

        var seatUnits = lines
            .Where(line => line.Kind == PlanChangeLineKind.Seats)
            .ToDictionary(line => line.Code, line => line.UnitAmountCents, StringComparer.Ordinal);
        foreach (var seat in seats.Where(seat => seat.Status == SeatStatus.Active))
            // Sin línea = un tipo sin costo: cambia de ciclo a $0, igual que se cobró.
            prices[seat.Id] = Money.Create(seatUnits.GetValueOrDefault(seat.Type.ToString()) / 100m, currency).Value;

        return prices;
    }

    private static PlanChangeQuote BlockedBy(Error error) => new([], 0, 0, error);

    /// <summary>Lo que se ahorra frente a seguir pagando mes a mes lo que paga hoy (precios contratados).</summary>
    private static long Savings(
        TenantSubscription subscription,
        PlanChangeResolution change,
        IEnumerable<TenantAddOn> addOns,
        IEnumerable<SubscriptionSeat> seats,
        long totalCents
    )
    {
        if (subscription.BillingCycle != BillingCycle.Monthly || change.EffectiveCycle != BillingCycle.Yearly)
            return 0;

        var monthly =
            change.CurrentAmountCents
            + addOns.Sum(addOn => Cents(addOn.UnitPrice) * addOn.Quantity)
            + seats.Sum(seat => Cents(seat.UnitPrice));
        return Math.Max(0, monthly * 12 - totalCents);
    }

    private static long Cents(Money money) => (long)Math.Round(money.Amount * 100m, MidpointRounding.AwayFromZero);

    private static string CycleLabel(BillingCycle cycle) =>
        cycle switch
        {
            BillingCycle.Yearly => "yearly",
            BillingCycle.Monthly => "monthly",
            _ => cycle.ToString().ToLowerInvariant(),
        };
}
