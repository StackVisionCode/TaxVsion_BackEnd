using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Subscription.Domain.Entitlements;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Domain.Plans;

/// <summary>
/// Límite o flag incluido por default en una versión de plan (ej. "seats.max" = "3").
/// Entidad hija de <see cref="SubscriptionPlanVersion"/>: su configuración EF requiere
/// ValueGeneratedNever() (ver guardrail de persistencia).
/// </summary>
public sealed class PlanEntitlementDefinition : BaseEntity
{
    public Guid PlanVersionId { get; private set; }
    public EntitlementKey Key { get; private set; } = null!;
    public EntitlementValueType ValueType { get; private set; }
    public string DefaultValue { get; private set; } = default!;
    public string Description { get; private set; } = default!;

    private PlanEntitlementDefinition() { }

    public static Result<PlanEntitlementDefinition> Create(
        Guid planVersionId,
        EntitlementKey key,
        EntitlementValueType valueType,
        string defaultValue,
        string description
    )
    {
        if (planVersionId == Guid.Empty)
        {
            return Result.Failure<PlanEntitlementDefinition>(
                new Error("PlanEntitlementDefinition.InvalidVersion", "PlanVersionId is required.")
            );
        }

        if (defaultValue is null)
        {
            return Result.Failure<PlanEntitlementDefinition>(
                new Error("PlanEntitlementDefinition.InvalidDefaultValue", "DefaultValue is required.")
            );
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            return Result.Failure<PlanEntitlementDefinition>(
                new Error("PlanEntitlementDefinition.InvalidDescription", "Description is required.")
            );
        }

        return Result.Success(
            new PlanEntitlementDefinition
            {
                PlanVersionId = planVersionId,
                Key = key,
                ValueType = valueType,
                DefaultValue = defaultValue,
                Description = description,
            }
        );
    }
}
