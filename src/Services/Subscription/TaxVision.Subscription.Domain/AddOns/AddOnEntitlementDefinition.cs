using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Subscription.Domain.Entitlements;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Domain.AddOns;

/// <summary>Entidad hija de <see cref="AddOnDefinition"/>: su configuración EF requiere
/// ValueGeneratedNever() (ver guardrail de persistencia).</summary>
public sealed class AddOnEntitlementDefinition : BaseEntity
{
    public Guid AddOnDefinitionId { get; private set; }
    public EntitlementKey Key { get; private set; } = null!;
    public EntitlementValueType ValueType { get; private set; }
    public string Value { get; private set; } = default!;
    public AddOnMergeStrategy MergeStrategy { get; private set; }

    private AddOnEntitlementDefinition() { }

    public static Result<AddOnEntitlementDefinition> Create(
        Guid addOnDefinitionId,
        EntitlementKey key,
        EntitlementValueType valueType,
        string value,
        AddOnMergeStrategy mergeStrategy
    )
    {
        if (addOnDefinitionId == Guid.Empty)
        {
            return Result.Failure<AddOnEntitlementDefinition>(
                new Error("AddOnEntitlementDefinition.InvalidDefinition", "AddOnDefinitionId is required.")
            );
        }

        if (value is null)
        {
            return Result.Failure<AddOnEntitlementDefinition>(
                new Error("AddOnEntitlementDefinition.InvalidValue", "Value is required.")
            );
        }

        return Result.Success(
            new AddOnEntitlementDefinition
            {
                AddOnDefinitionId = addOnDefinitionId,
                Key = key,
                ValueType = valueType,
                Value = value,
                MergeStrategy = mergeStrategy,
            }
        );
    }
}
