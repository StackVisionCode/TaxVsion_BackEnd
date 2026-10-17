namespace TaxVision.Subscription.Application.Plans.Commands.ReplaceDraftVersion;

/// <summary>CAT-F2.4 — reemplaza el contenido completo de una versión en estado Draft:
/// módulos (que se persisten como features con key <c>module.*</c>), features no-módulo,
/// entitlements, price tiers, trial y ciclos de facturación. Si la versión ya está publicada
/// devuelve 409.</summary>
public sealed record ReplaceDraftVersionCommand(
    Guid PlanId,
    Guid VersionId,
    IReadOnlyList<string> Modules,
    IReadOnlyList<DraftFeatureInput> Features,
    IReadOnlyList<DraftEntitlementInput> Entitlements,
    IReadOnlyList<DraftPriceTierInput> PriceTiers,
    int TrialDaysDefault,
    IReadOnlyList<string> SupportedBillingCycles,
    Guid ActorUserId
);

public sealed record DraftFeatureInput(string Key, bool DefaultEnabled, string Description);

public sealed record DraftEntitlementInput(string Key, string ValueType, string DefaultValue, string Description);

public sealed record DraftPriceTierInput(
    string BillingCycle,
    int MinQuantity,
    int? MaxQuantity,
    decimal Amount,
    string Currency
);
