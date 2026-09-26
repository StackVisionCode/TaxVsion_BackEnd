using BuildingBlocks.Results;
using TaxVision.Subscription.Application.Abstractions;
using TaxVision.Subscription.Application.AddOns;
using TaxVision.Subscription.Application.Common;
using TaxVision.Subscription.Domain.AddOns;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.Seats;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Application.Subscriptions.Queries;

/// <summary>
/// Read model del Account: plan contratado, período, cambio pendiente, asientos y catálogo de add-ons con
/// su elegibilidad, en una sola llamada para no encadenar cuatro desde el navegador.
/// </summary>
public static class GetAccountSubscriptionHandler
{
    /// <summary>Solo aplica a un plan sin tarifa cargada, donde el monto va en cero.</summary>
    private const string DefaultCurrency = "USD";

    /// <summary>Un asiento en estos estados ya no cuenta, mismo criterio que el snapshot de entitlements.</summary>
    private static readonly SeatStatus[] DeadSeatStatuses =
    [
        SeatStatus.Cancelled,
        SeatStatus.Expired,
        SeatStatus.Released,
    ];

    public static async Task<Result<AccountSubscriptionResponse>> Handle(
        GetAccountSubscriptionQuery query,
        ISubscriptionRepository subscriptions,
        IPlanRepository plans,
        ISubscriptionSeatRepository seats,
        IAddOnDefinitionRepository addOnDefinitions,
        ITenantAddOnRepository tenantAddOns,
        ISeatPurchaseIntentRepository seatIntents,
        CancellationToken ct
    )
    {
        var subscription = await subscriptions.GetByTenantIdAsync(query.TenantId, ct);
        if (subscription is null)
            return Result.Failure<AccountSubscriptionResponse>(
                new Error("Subscription.NotFound", "Subscription does not exist.")
            );

        var plan = await plans.GetByIdAsync(subscription.PlanId, ct);
        if (plan is null)
            return Result.Failure<AccountSubscriptionResponse>(new Error("Plan.NotFound", "Plan does not exist."));

        // La versión CONTRATADA, no la publicada: el tenant conserva el precio y los límites que firmó.
        var version =
            plan.Versions.FirstOrDefault(candidate => candidate.Id == subscription.PlanVersionId)
            ?? plan.GetPublishedVersion();
        if (version is null)
            return Result.Failure<AccountSubscriptionResponse>(
                new Error("Plan.NoPublishedVersion", "Plan has no published version.")
            );

        var price = PlanPricing.ResolveBaseSubscriptionPrice(version, subscription.BillingCycle);
        var enabledModules = PlanVersionEntitlements.GetEnabledModules(version);

        var planView = new AccountPlanView(
            plan.Code.Value,
            plan.Name,
            subscription.Status.ToString(),
            subscription.BillingCycle.ToString(),
            price?.AmountCents ?? 0,
            price?.Currency ?? DefaultCurrency,
            enabledModules,
            // Mismo criterio que TenantSubscriptionAccessConsumer en Auth: acá se corta el acceso.
            subscription.Status
                is SubscriptionStatus.Suspended
                    or SubscriptionStatus.Expired
        );

        var period = new AccountPeriodView(
            subscription.CurrentPeriodStartUtc,
            subscription.CurrentPeriodEndUtc,
            subscription.NextRenewalAtUtc,
            subscription.TrialEndsAtUtc,
            subscription.GracePeriodEndsAtUtc,
            subscription.CancelAtPeriodEnd,
            subscription.CancelledAtUtc
        );

        var includedSeats = PlanVersionEntitlements.GetInt(version, "seats.max", fallback: 0);
        var purchasedSeats = (await seats.GetByTenantIdAsync(query.TenantId, ct)).Count(seat =>
            !DeadSeatStatuses.Contains(seat.Status)
        );

        var addOns = BuildAddOns(
            await addOnDefinitions.GetPublishedAsync(ct),
            await tenantAddOns.GetByTenantIdAsync(query.TenantId, ct),
            enabledModules,
            subscription.BillingCycle
        );

        var nowUtc = DateTime.UtcNow;
        var openCheckout = await seatIntents.GetOpenByTenantAsync(query.TenantId, nowUtc, ct);

        return Result.Success(
            new AccountSubscriptionResponse(
                planView,
                period,
                PendingPlanChangeProjection.From(subscription),
                new AccountSeatsView(includedSeats, purchasedSeats, includedSeats + purchasedSeats),
                addOns,
                openCheckout is null || !openCheckout.IsOpen(nowUtc)
                    ? null
                    : new AccountOpenSeatCheckoutView(
                        openCheckout.Id,
                        openCheckout.SeatType.ToString(),
                        openCheckout.Quantity,
                        openCheckout.ProratedTotalCents,
                        openCheckout.Currency,
                        openCheckout.CheckoutUrl!,
                        openCheckout.CheckoutExpiresAtUtc!.Value
                    )
            )
        );
    }

    private static List<AccountAddOnView> BuildAddOns(
        IReadOnlyList<AddOnDefinition> definitions,
        IReadOnlyList<TenantAddOn> owned,
        IReadOnlyList<string> enabledModules,
        BillingCycle billingCycle
    )
    {
        var activeByCode = owned
            .Where(addOn => addOn.Status == AddOnStatus.Active)
            .GroupBy(addOn => addOn.AddOnCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var views = new List<AccountAddOnView>(definitions.Count);
        foreach (var definition in definitions)
        {
            var active = activeByCode.GetValueOrDefault(definition.Code.Value);
            // Con el add-on activo vale lo que el tenant paga hoy; si no, lo que costaría comprarlo.
            var catalogPrice = definition.ResolveUnitPrice(billingCycle, quantity: 1);
            var price = active?.UnitPrice ?? (catalogPrice.IsSuccess ? catalogPrice.Value : null);
            var eligibility =
                active is not null ? AddOnEligibility.Active
                : AddOnEligibilityRules.IsIncludedInPlan(definition, enabledModules) ? AddOnEligibility.Included
                : AddOnEligibility.Available;

            views.Add(
                new AccountAddOnView(
                    definition.Code.Value,
                    definition.Name,
                    definition.Description,
                    definition.Category,
                    eligibility,
                    price is null ? null : ToCents(price),
                    price?.Currency,
                    active?.Id,
                    active?.CurrentPeriodEndUtc,
                    active?.AutoRenew
                )
            );
        }

        return views;
    }

    private static long ToCents(Money amount) => (long)Math.Round(amount.Amount * 100m, MidpointRounding.AwayFromZero);
}
