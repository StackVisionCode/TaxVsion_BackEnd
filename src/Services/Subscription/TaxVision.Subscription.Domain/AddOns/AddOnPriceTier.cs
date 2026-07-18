using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Domain.AddOns;

/// <summary>Entidad hija de <see cref="AddOnDefinition"/>: su configuración EF requiere
/// ValueGeneratedNever() (ver guardrail de persistencia).</summary>
public sealed class AddOnPriceTier : BaseEntity
{
    public Guid AddOnDefinitionId { get; private set; }
    public BillingCycle BillingCycle { get; private set; }
    public int MinQuantity { get; private set; }
    public int? MaxQuantity { get; private set; }
    public Money UnitAmount { get; private set; } = null!;

    private AddOnPriceTier() { }

    public static Result<AddOnPriceTier> Create(
        Guid addOnDefinitionId,
        BillingCycle billingCycle,
        int minQuantity,
        int? maxQuantity,
        Money unitAmount
    )
    {
        if (addOnDefinitionId == Guid.Empty)
            return Result.Failure<AddOnPriceTier>(
                new Error("AddOnPriceTier.InvalidDefinition", "AddOnDefinitionId is required.")
            );

        if (minQuantity < 0)
            return Result.Failure<AddOnPriceTier>(
                new Error("AddOnPriceTier.InvalidMinQuantity", "MinQuantity cannot be negative.")
            );

        if (maxQuantity is not null && maxQuantity < minQuantity)
            return Result.Failure<AddOnPriceTier>(
                new Error("AddOnPriceTier.InvalidRange", "MaxQuantity cannot be less than MinQuantity.")
            );

        return Result.Success(
            new AddOnPriceTier
            {
                AddOnDefinitionId = addOnDefinitionId,
                BillingCycle = billingCycle,
                MinQuantity = minQuantity,
                MaxQuantity = maxQuantity,
                UnitAmount = unitAmount,
            }
        );
    }
}
