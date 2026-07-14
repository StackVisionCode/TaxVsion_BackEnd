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
