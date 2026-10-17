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
    private static readonly SeatStatus[] DeadSeatStatuses = [SeatStatus.Cancelled, SeatStatus.Expired];

    public static async Task<Result<AccountSubscriptionResponse>> Handle(
        GetAccountSubscriptionQuery query,
        ISubscriptionRepository subscriptions,
        IPlanRepository plans,
        ISubscriptionSeatRepository seats,
        IAddOnDefinitionRepository addOnDefinitions,
        ITenantAddOnRepository tenantAddOns,
        ISeatPurchaseIntentRepository seatIntents,
        IAddOnPurchaseIntentRepository addOnIntents,
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

        // La versión CONTRATADA, no la publicada: el tenant conserva el PRECIO y los LÍMITES que firmó.
        var version =
            plan.Versions.FirstOrDefault(candidate => candidate.Id == subscription.PlanVersionId)
            ?? plan.GetPublishedVersion();
        if (version is null)
            return Result.Failure<AccountSubscriptionResponse>(
                new Error("Plan.NoPublishedVersion", "Plan has no published version.")
            );

        var price = PlanPricing.ResolveBaseSubscriptionPrice(version, subscription.BillingCycle);

        // Los MÓDULOS, en cambio, salen de la versión publicada — la misma que usa
        // `EntitlementSnapshotBuilder`, que es lo que de verdad concede o deniega el acceso.
        //
        // Leerlos de la versión contratada hacía que el sistema se contradijera en cuanto se revisaba
        // un plan: el snapshot le daba el módulo nuevo al tenant (el gate lo dejaba entrar) mientras
        // esta pantalla seguía sin listarlo, y el add-on correspondiente aparecía EN VENTA para alguien
        // que ya lo tenía. La regla de "conservas lo que firmaste" aplica al precio, no a qué features
        // están encendidas: eso lo decide el snapshot y aquí solo se refleja.
        var publishedVersion = plan.GetPublishedVersion() ?? version;
        var enabledModules = PlanVersionEntitlements.GetEnabledModules(publishedVersion);

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
            subscription.CancelAtPeriodEnd,
            subscription.CancelledAtUtc
        );

        var includedSeats = PlanVersionEntitlements.GetInt(version, "seats.max", fallback: 0);
        var purchasedSeats = (await seats.GetByTenantIdAsync(query.TenantId, ct)).Count(seat =>
            !DeadSeatStatuses.Contains(seat.Status)
        );

        var definitions = await addOnDefinitions.GetPublishedAsync(ct);
        var addOns = BuildAddOns(
            subscription,
            definitions,
            await tenantAddOns.GetByTenantIdAsync(query.TenantId, ct),
            enabledModules,
            subscription.BillingCycle
        );

        var nowUtc = DateTime.UtcNow;
        var openCheckout = await seatIntents.GetOpenByTenantAsync(query.TenantId, nowUtc, ct);
        var openAddOnCheckouts = BuildOpenAddOnCheckouts(
            await addOnIntents.ListOpenByTenantAsync(query.TenantId, nowUtc, ct),
            definitions,
            nowUtc
        );

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
                    ),
                openAddOnCheckouts
            )
        );
    }

    /// <summary>
    /// Mapea cada intención abierta a su tarjeta en el Account. El display name sale del catálogo publicado
    /// — es el mismo campo que la lista de add-ons usa como respaldo cuando el módulo no tiene etiqueta.
    /// </summary>
    private static IReadOnlyList<AccountOpenAddOnCheckoutView> BuildOpenAddOnCheckouts(
        IReadOnlyList<AddOnPurchaseIntent> intents,
        IReadOnlyList<AddOnDefinition> definitions,
        DateTime nowUtc
    )
    {
        if (intents.Count == 0)
            return [];

        var displayByCode = definitions.ToDictionary(
            definition => definition.Code.Value,
            definition => definition.Name,
            StringComparer.OrdinalIgnoreCase
        );

        var views = new List<AccountOpenAddOnCheckoutView>(intents.Count);
        foreach (var intent in intents)
        {
            // Segunda guardia: el repo ya filtra, pero reusar `IsOpen` evita emitir una tarjeta sin URL.
            if (!intent.IsOpen(nowUtc))
                continue;

            views.Add(
                new AccountOpenAddOnCheckoutView(
                    intent.Id,
                    intent.AddOnCode,
                    displayByCode.GetValueOrDefault(intent.AddOnCode, intent.AddOnCode),
                    intent.Quantity,
                    intent.ProratedTotalCents,
                    intent.Currency,
                    intent.CheckoutUrl!,
                    intent.CheckoutExpiresAtUtc!.Value
                )
            );
        }

        return views;
    }

    /// <summary>
    /// La misma lista blanca que <c>AddOnPurchaseEligibility</c>: fuera de estos estados el guard de compra
    /// rechaza cualquier add-on, así que el read-model no debe pintarlo "Available".
    /// </summary>
    private static readonly SubscriptionStatus[] AddOnPurchasableStatuses =
    [
        SubscriptionStatus.Trialing,
        SubscriptionStatus.Active,
    ];

    private static List<AccountAddOnView> BuildAddOns(
        TenantSubscription subscription,
        IReadOnlyList<AddOnDefinition> definitions,
        IReadOnlyList<TenantAddOn> owned,
        IReadOnlyList<string> enabledModules,
        BillingCycle billingCycle
    )
    {
        // Si la suscripción no está en estado de compra (PastDue, Suspended, Expired…) el POST rechaza con
        // Subscription.CannotPurchaseAddOns, así que ningún add-on sin comprar debe aparecer "Available".
        var baseBlocked = Array.IndexOf(AddOnPurchasableStatuses, subscription.Status) < 0;
        // Un tenant puede tener N instancias del mismo SKU apilable (storage pack × N). Se agrupan por
        // código: el conteo alimenta la UI ("+10 GB × 3") y la primera instancia da fecha de renovación
        // y autoRenew para mostrar en el card. Las N comparten UnitPrice (tier fijo).
        var activeByCode = owned
            .Where(addOn => addOn.Status == AddOnStatus.Active)
            .GroupBy(addOn => addOn.AddOnCode, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.ToList(), StringComparer.OrdinalIgnoreCase);
        // El guard de compra lee MutexGroup del catálogo (no de TenantAddOn); acá se precalcula una vez para
        // que la evaluación del read-model no dispare queries extra por add-on.
        var mutexGroupByCode = AddOnEligibilityRules.MutexGroupLookupFor(definitions);

        var views = new List<AccountAddOnView>(definitions.Count);
        foreach (var definition in definitions)
        {
            var activeInstances = activeByCode.GetValueOrDefault(definition.Code.Value) ?? [];
            var first = activeInstances.FirstOrDefault();
            // Con el add-on activo vale lo que el tenant paga hoy; si no, lo que costaría comprarlo.
            var catalogPrice = definition.ResolveUnitPrice(billingCycle, quantity: 1);
            var price = first?.UnitPrice ?? (catalogPrice.IsSuccess ? catalogPrice.Value : null);
            var eligibility =
                first is not null ? AddOnEligibility.Active
                : AddOnEligibilityRules.IsIncludedInPlan(definition, enabledModules) ? AddOnEligibility.Included
                : AddOnEligibilityRules.BlockedByMutex(definition, owned, mutexGroupByCode) ? AddOnEligibility.Blocked
                : baseBlocked ? AddOnEligibility.Blocked
                : AddOnEligibility.Available;
            // Permitir comprar OTRA instancia del mismo SKU solo cuando: el catálogo lo marca apilable, el
            // tenant está en estado comprable y, si el SKU tiene mutex group, el tenant no tiene otro tier
            // del grupo activo (el guard mutex ya protege eso, acá se refleja en el flag UI).
            var canStack =
                definition.AllowMultipleInstances
                && !baseBlocked
                && !AddOnEligibilityRules.BlockedByMutex(definition, owned, mutexGroupByCode);

            views.Add(
                new AccountAddOnView(
                    definition.Code.Value,
                    definition.Name,
                    AddOnEligibilityRules.ModulesOf(definition),
                    definition.Description,
                    definition.Category,
                    eligibility,
                    price is null ? null : ToCents(price),
                    price?.Currency,
                    first?.Id,
                    first?.CurrentPeriodEndUtc,
                    first?.AutoRenew,
                    activeInstances.Count,
                    canStack
                )
            );
        }

        return views;
    }

    private static long ToCents(Money amount) => (long)Math.Round(amount.Amount * 100m, MidpointRounding.AwayFromZero);
}
