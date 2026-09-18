using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Domain.Seats;

/// <summary>Precio de un asiento de un <see cref="SeatType"/> y <see cref="ValueObjects.BillingCycle"/>.
/// Entidad hija de <see cref="SeatPricing"/>: su configuración EF requiere <c>ValueGeneratedNever()</c>
/// (guardrail de persistencia). Molde: <c>AddOnPriceTier</c>.</summary>
public sealed class SeatPriceTier : BaseEntity
{
    public Guid SeatPricingId { get; private set; }
    public SeatType SeatType { get; private set; }
    public BillingCycle BillingCycle { get; private set; }
    public Money UnitAmount { get; private set; } = null!;

    private SeatPriceTier() { }

    public static Result<SeatPriceTier> Create(
        Guid seatPricingId,
        SeatType seatType,
        BillingCycle billingCycle,
        Money unitAmount
    )
    {
        if (seatPricingId == Guid.Empty)
            return Result.Failure<SeatPriceTier>(
                new Error("SeatPriceTier.InvalidCatalog", "SeatPricingId is required.")
            );

        return Result.Success(
            new SeatPriceTier
            {
                SeatPricingId = seatPricingId,
                SeatType = seatType,
                BillingCycle = billingCycle,
                UnitAmount = unitAmount,
            }
        );
    }

    /// <summary>Cambia el precio del tramo (edición por PlatformAdmin). Lo orquesta <see cref="SeatPricing"/>.</summary>
    internal void ChangeAmount(Money unitAmount) => UnitAmount = unitAmount;
}
