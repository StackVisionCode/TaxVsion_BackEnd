using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Domain.Plans;

/// <summary>
/// Feature de dominio incluida por default en una versión de plan (ej. "signatures.enabled").
/// Entidad hija de <see cref="SubscriptionPlanVersion"/>: su Id se genera en esta factory y
/// cuelga de la navegación HasMany del padre, por lo que su configuración EF requiere
/// ValueGeneratedNever() (ver guardrail de persistencia).
/// </summary>
public sealed class PlanFeature : BaseEntity
{
    public Guid PlanVersionId { get; private set; }
    public EntitlementKey FeatureKey { get; private set; } = null!;
    public bool DefaultEnabled { get; private set; }
    public string Description { get; private set; } = default!;

    private PlanFeature() { }

    public static Result<PlanFeature> Create(
        Guid planVersionId,
        EntitlementKey featureKey,
        bool defaultEnabled,
        string description
    )
    {
        if (planVersionId == Guid.Empty)
            return Result.Failure<PlanFeature>(new Error("PlanFeature.InvalidVersion", "PlanVersionId is required."));

        if (string.IsNullOrWhiteSpace(description))
            return Result.Failure<PlanFeature>(new Error("PlanFeature.InvalidDescription", "Description is required."));

        return Result.Success(
            new PlanFeature
            {
                PlanVersionId = planVersionId,
                FeatureKey = featureKey,
                DefaultEnabled = defaultEnabled,
                Description = description,
            }
        );
    }
}
