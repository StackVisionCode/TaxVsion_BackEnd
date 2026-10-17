namespace TaxVision.Subscription.Application.Plans.Queries;

/// <summary>CAT-F2.2 — vista completa del plan para la pantalla de autoría. Incluye TODAS las
/// versiones (Draft/Published/Superseded/Archived) con sus features, entitlements y price tiers.
/// La vista pública (<see cref="PlanResponse"/>) sólo expone la versión publicada.</summary>
public sealed record PlanAdminResponse(
    Guid Id,
    string Code,
    string Name,
    string Description,
    string Tier,
    string Status,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc,
    IReadOnlyList<PlanVersionAdminResponse> Versions
);

public sealed record PlanVersionAdminResponse(
    Guid Id,
    int VersionNumber,
    string Status,
    DateTime EffectiveFromUtc,
    DateTime? EffectiveUntilUtc,
    int TrialDaysDefault,
    IReadOnlyList<string> SupportedBillingCycles,
    IReadOnlyList<PlanFeatureAdminResponse> Features,
    IReadOnlyList<PlanEntitlementAdminResponse> Entitlements,
    IReadOnlyList<PlanPriceTierAdminResponse> PriceTiers
);

public sealed record PlanFeatureAdminResponse(string Key, bool DefaultEnabled, string Description);

public sealed record PlanEntitlementAdminResponse(
    string Key,
    string ValueType,
    string DefaultValue,
    string Description
);

public sealed record PlanPriceTierAdminResponse(
    string BillingCycle,
    int MinQuantity,
    int? MaxQuantity,
    decimal Amount,
    string Currency
);
