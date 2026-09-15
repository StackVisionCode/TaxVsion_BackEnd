using System.Linq;
using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Domain.Plans;

/// <summary>
/// Catálogo de un plan comercial del SaaS. El plan en sí es estable; sus términos
/// comerciales (precio, features, límites) viven versionados en <see cref="SubscriptionPlanVersion"/>.
/// Solo una versión puede estar en <see cref="PlanVersionStatus.Published"/> a la vez.
/// </summary>
public sealed class SubscriptionPlan : BaseEntity
{
    private readonly List<SubscriptionPlanVersion> _versions = [];

    public PlanCode Code { get; private set; } = null!;
    public string Name { get; private set; } = default!;
    public string Description { get; private set; } = default!;
    public PlanTier Tier { get; private set; }
    public PlanStatus Status { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public Guid CreatedBy { get; private set; }
    public Guid UpdatedBy { get; private set; }

    public IReadOnlyCollection<SubscriptionPlanVersion> Versions => _versions;

    private SubscriptionPlan() { }

    public static Result<SubscriptionPlan> Create(
        PlanCode code,
        string name,
        string description,
        PlanTier tier,
        Guid actorUserId,
        DateTime nowUtc
    )
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 200)
            return Result.Failure<SubscriptionPlan>(
                new Error("Plan.InvalidName", "Name is required and must be 200 characters or fewer.")
            );

        if (string.IsNullOrWhiteSpace(description) || description.Length > 2000)
        {
            return Result.Failure<SubscriptionPlan>(
                new Error("Plan.InvalidDescription", "Description is required and must be 2000 characters or fewer.")
            );
        }

        return Result.Success(
            new SubscriptionPlan
            {
                Code = code,
                Name = name,
                Description = description,
                Tier = tier,
                Status = PlanStatus.Draft,
                CreatedAtUtc = nowUtc,
                UpdatedAtUtc = nowUtc,
                CreatedBy = actorUserId,
                UpdatedBy = actorUserId,
            }
        );
    }

    /// <summary>
    /// Factory reservada para el catálogo inicial sembrado en el arranque del servicio
    /// (ver <c>SubscriptionPlanCatalogSeeder</c>). Reutiliza <see cref="Create"/> y solo
    /// fija un Id determinista para que el catálogo sea estable entre entornos.
    /// </summary>
    public static Result<SubscriptionPlan> Seed(
        Guid id,
        PlanCode code,
        string name,
        string description,
        PlanTier tier,
        DateTime nowUtc
    )
    {
        var created = Create(code, name, description, tier, actorUserId: Guid.Empty, nowUtc);
        if (created.IsFailure)
            return created;

        created.Value.Id = id;
        return created;
    }

    public Result AddVersion(SubscriptionPlanVersion version, Guid actorUserId, DateTime nowUtc)
    {
        if (version.PlanId != Id)
            return Result.Failure(new Error("Plan.VersionMismatch", "Version does not belong to this plan."));

        _versions.Add(version);
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    public Result PublishVersion(Guid versionId, DateTime effectiveFromUtc, Guid actorUserId, DateTime nowUtc)
    {
        var target = FindVersionById(versionId);
        if (target is null)
            return Result.Failure(new Error("Plan.VersionNotFound", "Version does not exist on this plan."));

        var currentlyPublished = FindPublishedVersion();
        if (currentlyPublished is not null)
        {
            var supersedeResult = currentlyPublished.Supersede(nowUtc);
            if (supersedeResult.IsFailure)
                return supersedeResult;
        }

        var publishResult = target.Publish(effectiveFromUtc);
        if (publishResult.IsFailure)
            return publishResult;

        if (Status == PlanStatus.Draft)
            Status = PlanStatus.Published;

        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    public Result Deprecate(Guid actorUserId, DateTime nowUtc)
    {
        if (Status == PlanStatus.Archived)
            return Result.Failure(new Error("Plan.AlreadyArchived", "Plan is already archived."));

        Status = PlanStatus.Deprecated;
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    public Result Archive(Guid actorUserId, DateTime nowUtc)
    {
        if (Status != PlanStatus.Deprecated)
            return Result.Failure(new Error("Plan.NotDeprecated", "Only a deprecated plan can be archived."));

        Status = PlanStatus.Archived;
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    /// <summary>
    /// Publica una versión nueva idéntica a la actual pero con otro conjunto de módulos (module.*),
    /// superando la anterior. Una versión publicada es inmutable, por eso se versiona en vez de editar.
    /// Los límites, precios y features no-módulo se conservan.
    /// </summary>
    public Result ReviseModules(IReadOnlyCollection<string> modules, Guid actorUserId, DateTime nowUtc)
    {
        var published = FindPublishedVersion();
        if (published is null)
            return Result.Failure(new Error("Plan.NoPublishedVersion", "Plan has no published version to revise."));

        var draft = SubscriptionPlanVersion.Create(
            Id,
            published.VersionNumber + 1,
            published.TrialDaysDefault,
            published.SupportedBillingCycles.ToArray()
        );
        if (draft.IsFailure)
            return Result.Failure(draft.Error);

        var version = draft.Value;

        foreach (var entitlement in published.Entitlements)
        {
            var clone = PlanEntitlementDefinition.Create(
                version.Id,
                entitlement.Key,
                entitlement.ValueType,
                entitlement.DefaultValue,
                entitlement.Description
            );
            if (clone.IsFailure)
                return Result.Failure(clone.Error);
            version.AddEntitlementDefinition(clone.Value);
        }

        foreach (var tier in published.PriceTiers)
        {
            // UnitAmount es owned type: hay que clonar el Money, no reusar la instancia del tier viejo.
            var amount = Money.Create(tier.UnitAmount.Amount, tier.UnitAmount.Currency);
            if (amount.IsFailure)
                return Result.Failure(amount.Error);
            var clone = PlanPriceTier.Create(
                version.Id,
                tier.BillingCycle,
                tier.MinQuantity,
                tier.MaxQuantity,
                amount.Value
            );
            if (clone.IsFailure)
                return Result.Failure(clone.Error);
            version.AddPriceTier(clone.Value);
        }

        foreach (var feature in published.Features)
        {
            if (feature.FeatureKey.Value.StartsWith("module.", StringComparison.Ordinal))
                continue;
            var clone = PlanFeature.Create(version.Id, feature.FeatureKey, feature.DefaultEnabled, feature.Description);
            if (clone.IsFailure)
                return Result.Failure(clone.Error);
            version.AddFeature(clone.Value);
        }

        foreach (var module in modules.Select(m => m.Trim()).Where(m => m.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var key = EntitlementKey.Create($"module.{module}");
            if (key.IsFailure)
                return Result.Failure(key.Error);
            var feature = PlanFeature.Create(version.Id, key.Value, defaultEnabled: true, description: $"module.{module}");
            if (feature.IsFailure)
                return Result.Failure(feature.Error);
            version.AddFeature(feature.Value);
        }

        var added = AddVersion(version, actorUserId, nowUtc);
        if (added.IsFailure)
            return added;

        return PublishVersion(version.Id, nowUtc, actorUserId, nowUtc);
    }

    public SubscriptionPlanVersion? GetPublishedVersion() => FindPublishedVersion();

    private SubscriptionPlanVersion? FindVersionById(Guid versionId)
    {
        foreach (var version in _versions)
        {
            if (version.Id == versionId)
                return version;
        }

        return null;
    }

    private SubscriptionPlanVersion? FindPublishedVersion()
    {
        foreach (var version in _versions)
        {
            if (version.Status == PlanVersionStatus.Published)
                return version;
        }

        return null;
    }

    private void Touch(Guid actorUserId, DateTime nowUtc)
    {
        UpdatedAtUtc = nowUtc;
        UpdatedBy = actorUserId;
    }
}
