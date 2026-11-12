using System.Globalization;
using TaxVision.Subscription.Domain.Catalog;
using TaxVision.Subscription.Domain.Entitlements;
using TaxVision.Subscription.Domain.Plans;
using TaxVision.Subscription.Domain.Subscriptions;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Domain.Migrations;

/// <summary>
/// Qué cambia para una oficina si pasa de una edición a otra de su plan, en su ciclo de cobro. "Solo mejora" (D11):
/// el precio no sube, asientos, almacenamiento e invitaciones no bajan y no pierde ningún módulo.
/// </summary>
public sealed record VersionChangeComparison(
    long? PriceFromCents,
    long? PriceToCents,
    int? SeatsFrom,
    int? SeatsTo,
    long? StorageFromBytes,
    long? StorageToBytes,
    int? InvitationsFrom,
    int? InvitationsTo,
    IReadOnlyList<string> ModulesLost,
    IReadOnlyList<string> ModulesGained,
    BillingCycle Cycle,
    bool CycleAvailable
)
{
    public bool IsOnlyImprovement =>
        CycleAvailable
        && PriceToCents is { } to
        && (PriceFromCents is not { } from || to <= from)
        && NotLower(SeatsFrom, SeatsTo)
        && NotLower(StorageFromBytes, StorageToBytes)
        && NotLower(InvitationsFrom, InvitationsTo)
        && ModulesLost.Count == 0;

    public static VersionChangeComparison Compare(
        SubscriptionPlanVersion from,
        SubscriptionPlanVersion to,
        BillingCycle cycle,
        IReadOnlyCollection<Module> catalog
    )
    {
        var fromTerms = CommercialTerms.Of(from);
        var toTerms = CommercialTerms.Of(to);
        var fromModules = Modules(from, catalog);
        var toModules = Modules(to, catalog);

        return new VersionChangeComparison(
            Cents(fromTerms, cycle),
            Cents(toTerms, cycle),
            fromTerms.SeatsMax,
            toTerms.SeatsMax,
            fromTerms.StorageMaxBytes,
            toTerms.StorageMaxBytes,
            fromTerms.InvitationsMaxPending,
            toTerms.InvitationsMaxPending,
            fromModules.Except(toModules, StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToArray(),
            toModules.Except(fromModules, StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal).ToArray(),
            cycle,
            toTerms.Prices.ContainsKey(cycle)
        );
    }

    /// <summary>Resumen legible (en inglés, va al correo del cliente) de lo que cambia.</summary>
    public string Summary()
    {
        var parts = new List<string>();
        if (PriceFromCents != PriceToCents && PriceToCents is { } price)
            parts.Add($"price {Money(PriceFromCents)} → {Money(price)} per {CycleWord()}");
        if (SeatsFrom != SeatsTo)
            parts.Add(
                $"included users {SeatsFrom?.ToString(CultureInfo.InvariantCulture) ?? "—"} → {SeatsTo?.ToString(CultureInfo.InvariantCulture) ?? "—"}"
            );
        if (StorageFromBytes != StorageToBytes)
            parts.Add($"storage {Gb(StorageFromBytes)} → {Gb(StorageToBytes)}");
        if (InvitationsFrom != InvitationsTo)
            parts.Add(
                $"pending invitations {InvitationsFrom?.ToString(CultureInfo.InvariantCulture) ?? "—"} → {InvitationsTo?.ToString(CultureInfo.InvariantCulture) ?? "—"}"
            );
        if (ModulesGained.Count > 0)
            parts.Add($"adds {string.Join(", ", ModulesGained)}");
        if (ModulesLost.Count > 0)
            parts.Add($"removes {string.Join(", ", ModulesLost)}");
        return parts.Count == 0 ? "no changes to your price or limits" : string.Join("; ", parts);
    }

    private string CycleWord() =>
        Cycle == BillingCycle.Yearly ? "year"
        : Cycle == BillingCycle.Quarterly ? "quarter"
        : "month";

    private static bool NotLower(long? from, long? to) => from is null || (to is not null && to >= from);

    private static bool NotLower(int? from, int? to) => from is null || (to is not null && to >= from);

    private static long? Cents(CommercialTerms terms, BillingCycle cycle) =>
        terms.Prices.TryGetValue(cycle, out var money) ? (long)decimal.Round(money.Amount * 100m) : null;

    private static string[] Modules(SubscriptionPlanVersion version, IReadOnlyCollection<Module> catalog)
    {
        var enabled = ContractedEntitlementPolicy.EnabledModules(version, catalog);
        return enabled.IsSuccess ? [.. enabled.Value] : [];
    }

    private static string Money(long? cents) =>
        cents is { } value ? (value / 100m).ToString("$#,0.00", CultureInfo.InvariantCulture) : "—";

    private static string Gb(long? bytes) => bytes is { } value ? $"{value / (1024m * 1024 * 1024):0.##} GB" : "—";
}

/// <summary>Cuándo renueva una oficina: el fin de su período (o de su trial) y los siguientes, ciclo a ciclo.</summary>
public static class RenewalSchedule
{
    public static DateTime FirstRenewalOnOrAfter(TenantSubscription subscription, DateTime notBeforeUtc)
    {
        var next =
            subscription.Status == SubscriptionStatus.Trialing && subscription.TrialEndsAtUtc is { } trialEnd
                ? trialEnd
                : subscription.CurrentPeriodEndUtc;
        // Tope de seguridad: nunca más de 50 ciclos (un ciclo Custom sin días no avanza).
        for (var guard = 0; next < notBeforeUtc && guard < 50; guard++)
            next = subscription.BillingCycle.CalculateNext(next, customDays: 30);
        return next;
    }
}
