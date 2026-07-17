using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Domain.Plans;

/// <summary>
/// Precio de una versión de plan por ciclo de facturación y tramo de cantidad.
/// Entidad hija de <see cref="SubscriptionPlanVersion"/>: su configuración EF requiere
/// ValueGeneratedNever() (ver guardrail de persistencia).
/// </summary>
public sealed class PlanPriceTier : BaseEntity
{
    public Guid PlanVersionId { get; private set; }
    public BillingCycle BillingCycle { get; private set; }
    public int MinQuantity { get; private set; }
    public int? MaxQuantity { get; private set; }
    public Money UnitAmount { get; private set; } = null!;

    private PlanPriceTier() { }

    public static Result<PlanPriceTier> Create(
        Guid planVersionId,
        BillingCycle billingCycle,
        int minQuantity,
        int? maxQuantity,
        Money unitAmount
    )
    {
        if (planVersionId == Guid.Empty)
            return Result.Failure<PlanPriceTier>(
                new Error("PlanPriceTier.InvalidVersion", "PlanVersionId is required.")
            );

        if (minQuantity < 0)
            return Result.Failure<PlanPriceTier>(
                new Error("PlanPriceTier.InvalidMinQuantity", "MinQuantity cannot be negative.")
            );

        if (maxQuantity is not null && maxQuantity < minQuantity)
            return Result.Failure<PlanPriceTier>(
                new Error("PlanPriceTier.InvalidRange", "MaxQuantity cannot be less than MinQuantity.")
            );

        return Result.Success(
            new PlanPriceTier
            {
                PlanVersionId = planVersionId,
                BillingCycle = billingCycle,
                MinQuantity = minQuantity,
                MaxQuantity = maxQuantity,
                UnitAmount = unitAmount,
            }
        );
    }
}
