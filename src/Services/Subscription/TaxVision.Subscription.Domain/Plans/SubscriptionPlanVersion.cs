using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Domain.Plans;

/// <summary>
/// Snapshot inmutable (una vez publicado) de un plan en un momento comercial.
/// Entidad hija de <see cref="SubscriptionPlan"/>: su configuración EF requiere
/// ValueGeneratedNever() (ver guardrail de persistencia).
/// </summary>
public sealed class SubscriptionPlanVersion : BaseEntity
{
    private readonly List<PlanFeature> _features = [];
    private readonly List<PlanEntitlementDefinition> _entitlements = [];
    private readonly List<PlanPriceTier> _priceTiers = [];
    private readonly List<BillingCycle> _supportedBillingCycles = [];

    public Guid PlanId { get; private set; }
    public int VersionNumber { get; private set; }
    public PlanVersionStatus Status { get; private set; }
    public DateTime EffectiveFromUtc { get; private set; }
    public DateTime? EffectiveUntilUtc { get; private set; }
    public int TrialDaysDefault { get; private set; }

    public IReadOnlyCollection<PlanFeature> Features => _features;
    public IReadOnlyCollection<PlanEntitlementDefinition> Entitlements => _entitlements;
    public IReadOnlyCollection<PlanPriceTier> PriceTiers => _priceTiers;
    public IReadOnlyCollection<BillingCycle> SupportedBillingCycles => _supportedBillingCycles;

    private SubscriptionPlanVersion() { }

    public static Result<SubscriptionPlanVersion> Create(
        Guid planId,
        int versionNumber,
        int trialDaysDefault,
        IReadOnlyCollection<BillingCycle> supportedBillingCycles
    )
    {
        if (planId == Guid.Empty)
            return Result.Failure<SubscriptionPlanVersion>(new Error("PlanVersion.InvalidPlan", "PlanId is required."));

        if (versionNumber < 1)
        {
            return Result.Failure<SubscriptionPlanVersion>(
                new Error("PlanVersion.InvalidVersionNumber", "VersionNumber must be a positive integer.")
            );
        }

        if (trialDaysDefault is < 0 or > 90)
        {
            return Result.Failure<SubscriptionPlanVersion>(
                new Error("PlanVersion.InvalidTrialDays", "TrialDaysDefault must be between 0 and 90.")
            );
        }

        if (supportedBillingCycles.Count == 0)
        {
            return Result.Failure<SubscriptionPlanVersion>(
                new Error("PlanVersion.NoBillingCycles", "At least one billing cycle is required.")
            );
        }

        var version = new SubscriptionPlanVersion
        {
            PlanId = planId,
            VersionNumber = versionNumber,
            Status = PlanVersionStatus.Draft,
            TrialDaysDefault = trialDaysDefault,
        };
        version._supportedBillingCycles.AddRange(supportedBillingCycles);
        return Result.Success(version);
    }

    /// <summary>
    /// Factory reservada para el catálogo inicial sembrado en el arranque del servicio
    /// (ver <c>SubscriptionPlanCatalogSeeder</c>). Reutiliza <see cref="Create"/> y solo
    /// fija un Id determinista para que el catálogo sea estable entre entornos.
    /// </summary>
    public static Result<SubscriptionPlanVersion> Seed(
        Guid id,
        Guid planId,
        int versionNumber,
        int trialDaysDefault,
        IReadOnlyCollection<BillingCycle> supportedBillingCycles
    )
    {
        var created = Create(planId, versionNumber, trialDaysDefault, supportedBillingCycles);
        if (created.IsFailure)
            return created;

        created.Value.Id = id;
        return created;
    }

    public Result AddFeature(PlanFeature feature)
    {
        var guard = EnsureDraft();
        if (guard.IsFailure)
            return guard;

        _features.Add(feature);
        return Result.Success();
    }

    public Result AddEntitlementDefinition(PlanEntitlementDefinition entitlement)
    {
        var guard = EnsureDraft();
        if (guard.IsFailure)
            return guard;

        _entitlements.Add(entitlement);
        return Result.Success();
    }

    public Result AddPriceTier(PlanPriceTier tier)
    {
        var guard = EnsureDraft();
        if (guard.IsFailure)
            return guard;

        _priceTiers.Add(tier);
        return Result.Success();
    }

    public Result Publish(DateTime effectiveFromUtc)
    {
        if (Status != PlanVersionStatus.Draft)
            return Result.Failure(
                new Error("PlanVersion.InvalidTransition", $"Cannot publish a version from {Status}.")
            );

        // Caught live when a failed Draft update left v1 empty but Publish still returned 202,
        // creating an uncontractable plan on /subscription/plans. A version must carry at least
        // one feature/module and one price tier to be purchasable.
        if (_features.Count == 0)
            return Result.Failure(
                new Error("PlanVersion.NoFeatures", "Cannot publish a version without at least one feature or module.")
            );

        if (_priceTiers.Count == 0)
            return Result.Failure(
                new Error("PlanVersion.NoPriceTiers", "Cannot publish a version without at least one price tier.")
            );

        Status = PlanVersionStatus.Published;
        EffectiveFromUtc = effectiveFromUtc;
        return Result.Success();
    }

    public Result Supersede(DateTime nowUtc)
    {
        if (Status != PlanVersionStatus.Published)
            return Result.Failure(
                new Error("PlanVersion.InvalidTransition", $"Cannot supersede a version from {Status}.")
            );

        Status = PlanVersionStatus.Superseded;
        EffectiveUntilUtc = nowUtc;
        return Result.Success();
    }

    /// <summary>CAT-F2.4 — reemplazo total del contenido de la versión draft (features, entitlements,
    /// price tiers). Mantiene VersionNumber/Status/EffectiveFrom. El guard <see cref="EnsureDraft"/>
    /// garantiza que no se toca una versión publicada o superseded.</summary>
    public Result ReplaceDraftContent(
        IReadOnlyList<PlanFeature> features,
        IReadOnlyList<PlanEntitlementDefinition> entitlements,
        IReadOnlyList<PlanPriceTier> priceTiers,
        int trialDaysDefault,
        IReadOnlyCollection<BillingCycle> supportedBillingCycles
    )
    {
        var guard = EnsureDraft();
        if (guard.IsFailure)
            return guard;

        if (trialDaysDefault is < 0 or > 90)
            return Result.Failure(
                new Error("PlanVersion.InvalidTrialDays", "TrialDaysDefault must be between 0 and 90.")
            );

        if (supportedBillingCycles.Count == 0)
            return Result.Failure(new Error("PlanVersion.NoBillingCycles", "At least one billing cycle is required."));

        TrialDaysDefault = trialDaysDefault;
        _supportedBillingCycles.Clear();
        _supportedBillingCycles.AddRange(supportedBillingCycles);

        _features.Clear();
        _features.AddRange(features);
        _entitlements.Clear();
        _entitlements.AddRange(entitlements);
        _priceTiers.Clear();
        _priceTiers.AddRange(priceTiers);
        return Result.Success();
    }

    private Result EnsureDraft() =>
        Status == PlanVersionStatus.Draft
            ? Result.Success()
            : Result.Failure(new Error("PlanVersion.NotDraft", "Cannot modify a published or superseded version."));
}
