using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Domain.AddOns;

/// <summary>
/// Catálogo de un add-on disponible en la plataforma (ej. storage extra, firma premium).
/// A diferencia de <c>SubscriptionPlan</c> no está versionado: sus features/entitlements/
/// precios se editan añadiendo entradas mientras está en Draft y quedan fijos al publicar.
/// </summary>
public sealed class AddOnDefinition : BaseEntity
{
    private readonly List<AddOnFeature> _features = [];
    private readonly List<AddOnEntitlementDefinition> _entitlements = [];
    private readonly List<AddOnPriceTier> _priceTiers = [];
    private readonly List<BillingCycle> _supportedBillingCycles = [];

    public AddOnCode Code { get; private set; } = null!;
    public string Name { get; private set; } = default!;
    public string Description { get; private set; } = default!;
    public string Category { get; private set; } = default!;
    public AddOnDefinitionStatus Status { get; private set; }
    public bool AllowMultipleInstances { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public Guid CreatedBy { get; private set; }
    public Guid UpdatedBy { get; private set; }

    public IReadOnlyCollection<AddOnFeature> Features => _features;
    public IReadOnlyCollection<AddOnEntitlementDefinition> Entitlements => _entitlements;
    public IReadOnlyCollection<AddOnPriceTier> PriceTiers => _priceTiers;
    public IReadOnlyCollection<BillingCycle> SupportedBillingCycles => _supportedBillingCycles;

    private AddOnDefinition() { }

    public static Result<AddOnDefinition> Create(
        AddOnCode code,
        string name,
        string description,
        string category,
        bool allowMultipleInstances,
        IReadOnlyCollection<BillingCycle> supportedBillingCycles,
        Guid actorUserId,
        DateTime nowUtc
    )
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 200)
            return Result.Failure<AddOnDefinition>(
                new Error("AddOnDefinition.InvalidName", "Name is required and must be 200 characters or fewer.")
            );

        if (string.IsNullOrWhiteSpace(description) || description.Length > 2000)
        {
            return Result.Failure<AddOnDefinition>(
                new Error(
                    "AddOnDefinition.InvalidDescription",
                    "Description is required and must be 2000 characters or fewer."
                )
            );
        }

        if (supportedBillingCycles.Count == 0)
            return Result.Failure<AddOnDefinition>(
                new Error("AddOnDefinition.NoBillingCycles", "At least one billing cycle is required.")
            );

        var definition = new AddOnDefinition
        {
            Code = code,
            Name = name,
            Description = description,
            Category = category,
            Status = AddOnDefinitionStatus.Draft,
            AllowMultipleInstances = allowMultipleInstances,
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
            CreatedBy = actorUserId,
            UpdatedBy = actorUserId,
        };
        definition._supportedBillingCycles.AddRange(supportedBillingCycles);
        return Result.Success(definition);
    }

    /// <summary>Igual que <see cref="Create"/> pero con Id fijo, para el seed idempotente del catálogo.</summary>
    public static Result<AddOnDefinition> Seed(
        Guid id,
        AddOnCode code,
        string name,
        string description,
        string category,
        bool allowMultipleInstances,
        IReadOnlyCollection<BillingCycle> supportedBillingCycles,
        DateTime nowUtc
    )
    {
        var created = Create(
            code,
            name,
            description,
            category,
            allowMultipleInstances,
            supportedBillingCycles,
            Guid.Empty,
            nowUtc
        );
        if (created.IsFailure)
            return created;

        created.Value.Id = id;
        return created;
    }

    /// <summary>True si este add-on es de tipo módulo (features <c>module.*</c>) y TODOS sus módulos ya
    /// los cubre el plan destino, sin aportar cuota aditiva (Sum/Max) — entonces se absorbe en el upgrade.
    /// Un add-on con entitlements aditivos sigue sumando valor y NO se absorbe.</summary>
    public bool IsAbsorbedByPlanModules(IReadOnlyCollection<string> planModuleKeys)
    {
        foreach (var entitlement in _entitlements)
        {
            if (entitlement.MergeStrategy is AddOnMergeStrategy.Sum or AddOnMergeStrategy.Max)
                return false;
        }

        var hasModuleFeature = false;
        foreach (var feature in _features)
        {
            if (!feature.Enabled || !feature.FeatureKey.Value.StartsWith("module.", StringComparison.Ordinal))
                continue;

            hasModuleFeature = true;
            if (!planModuleKeys.Contains(feature.FeatureKey.Value))
                return false;
        }

        return hasModuleFeature;
    }

    /// <summary>Precio unitario del tramo que aplica al ciclo y la cantidad. El total lo calcula quien cobra (unitario × cantidad).</summary>
    public Result<Money> ResolveUnitPrice(BillingCycle billingCycle, int quantity)
    {
        foreach (var tier in _priceTiers)
        {
            if (
                tier.BillingCycle == billingCycle
                && quantity >= tier.MinQuantity
                && (tier.MaxQuantity is null || quantity <= tier.MaxQuantity)
            )
                return Result.Success(tier.UnitAmount);
        }

        return Result.Failure<Money>(
            new Error("AddOn.NoPriceTier", $"No price tier for cycle {billingCycle} and quantity {quantity}.")
        );
    }

    public Result AddFeature(AddOnFeature feature)
    {
        var guard = EnsureDraft();
        if (guard.IsFailure)
            return guard;

        _features.Add(feature);
        return Result.Success();
    }

    public Result AddEntitlementDefinition(AddOnEntitlementDefinition entitlement)
    {
        var guard = EnsureDraft();
        if (guard.IsFailure)
            return guard;

        _entitlements.Add(entitlement);
        return Result.Success();
    }

    public Result AddPriceTier(AddOnPriceTier tier)
    {
        var guard = EnsureDraft();
        if (guard.IsFailure)
            return guard;

        _priceTiers.Add(tier);
        return Result.Success();
    }

    /// <summary>Reemplaza los precios. A diferencia de features/entitlements, editable tras publicar:
    /// afecta compras NUEVAS; las vigentes conservan su precio en TenantAddOn.UnitPrice.</summary>
    public Result ReplacePriceTiers(IReadOnlyCollection<AddOnPriceTier> tiers, Guid actorUserId, DateTime nowUtc)
    {
        if (tiers.Count == 0)
            return Result.Failure(new Error("AddOnDefinition.NoPriceTiers", "At least one price tier is required."));

        foreach (var tier in tiers)
        {
            if (tier.AddOnDefinitionId != Id)
                return Result.Failure(
                    new Error("AddOnDefinition.TierMismatch", "A price tier does not belong to this add-on.")
                );
        }

        _priceTiers.Clear();
        _priceTiers.AddRange(tiers);
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    public Result Publish(Guid actorUserId, DateTime nowUtc)
    {
        if (Status != AddOnDefinitionStatus.Draft)
            return Result.Failure(new Error("AddOnDefinition.InvalidTransition", $"Cannot publish from {Status}."));

        Status = AddOnDefinitionStatus.Published;
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    public Result Deprecate(Guid actorUserId, DateTime nowUtc)
    {
        if (Status != AddOnDefinitionStatus.Published)
            return Result.Failure(new Error("AddOnDefinition.InvalidTransition", $"Cannot deprecate from {Status}."));

        Status = AddOnDefinitionStatus.Deprecated;
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    public Result Archive(Guid actorUserId, DateTime nowUtc)
    {
        if (Status != AddOnDefinitionStatus.Deprecated)
            return Result.Failure(
                new Error("AddOnDefinition.NotDeprecated", "Only a deprecated add-on can be archived.")
            );

        Status = AddOnDefinitionStatus.Archived;
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    private Result EnsureDraft() =>
        Status == AddOnDefinitionStatus.Draft
            ? Result.Success()
            : Result.Failure(
                new Error("AddOnDefinition.NotDraft", "Cannot modify a published, deprecated or archived add-on.")
            );

    private void Touch(Guid actorUserId, DateTime nowUtc)
    {
        UpdatedAtUtc = nowUtc;
        UpdatedBy = actorUserId;
    }
}
