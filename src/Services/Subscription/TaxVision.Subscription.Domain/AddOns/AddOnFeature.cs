using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Domain.AddOns;

/// <summary>Entidad hija de <see cref="AddOnDefinition"/>: su configuración EF requiere
/// ValueGeneratedNever() (ver guardrail de persistencia).</summary>
public sealed class AddOnFeature : BaseEntity
{
    public Guid AddOnDefinitionId { get; private set; }
    public EntitlementKey FeatureKey { get; private set; } = null!;
    public bool Enabled { get; private set; }

    private AddOnFeature() { }

    public static Result<AddOnFeature> Create(Guid addOnDefinitionId, EntitlementKey featureKey, bool enabled)
    {
        if (addOnDefinitionId == Guid.Empty)
            return Result.Failure<AddOnFeature>(new Error("AddOnFeature.InvalidDefinition", "AddOnDefinitionId is required."));

        return Result.Success(new AddOnFeature
        {
            AddOnDefinitionId = addOnDefinitionId,
            FeatureKey = featureKey,
            Enabled = enabled,
        });
    }
}
