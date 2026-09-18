using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Domain.Seats;

/// <summary>
/// Catálogo GLOBAL de precios de asiento (no por tenant), editable por PlatformAdmin — el equivalente
/// para seats de <c>AddOnDefinition</c>/<c>AddOnPriceTier</c>. Singleton: una sola fila con Id fijo
/// (<see cref="Seed"/>). Resuelve el precio por (tipo, ciclo). Cambiar precios afecta compras NUEVAS;
/// las vigentes conservan su precio en <c>SubscriptionSeat.UnitPrice</c> (misma semántica que
/// <c>ReplacePriceTiers</c> de add-ons).
/// </summary>
public sealed class SeatPricing : BaseEntity
{
    private readonly List<SeatPriceTier> _priceTiers = [];

    public DateTime CreatedAtUtc { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    public Guid UpdatedBy { get; private set; }

    public IReadOnlyCollection<SeatPriceTier> PriceTiers => _priceTiers;

    private SeatPricing() { }

    /// <summary>Crea el catálogo singleton con Id fijo (seed idempotente).</summary>
    public static SeatPricing Seed(Guid id, DateTime nowUtc)
    {
        var pricing = new SeatPricing
        {
            CreatedAtUtc = nowUtc,
            UpdatedAtUtc = nowUtc,
            UpdatedBy = Guid.Empty,
        };
        pricing.Id = id;
        return pricing;
    }

    /// <summary>Añade un tramo de precio (usado por el seed). El tramo debe pertenecer a este catálogo.</summary>
    public Result AddPriceTier(SeatPriceTier tier)
    {
        if (tier.SeatPricingId != Id)
            return Result.Failure(
                new Error("SeatPricing.TierMismatch", "A price tier does not belong to this catalog.")
            );

        _priceTiers.Add(tier);
        return Result.Success();
    }

    /// <summary>Precio unitario del asiento por tipo y ciclo. Falla si no hay tramo configurado.</summary>
    public Result<Money> ResolveUnitPrice(SeatType seatType, BillingCycle billingCycle)
    {
        foreach (var tier in _priceTiers)
        {
            if (tier.SeatType == seatType && tier.BillingCycle == billingCycle)
                return Result.Success(tier.UnitAmount);
        }

        return Result.Failure<Money>(
            new Error("SeatPricing.NoPriceTier", $"No seat price for type {seatType} and cycle {billingCycle}.")
        );
    }

    /// <summary>Fija el precio mensual y anual de un tipo de asiento (PlatformAdmin), upsert por tramo.
    /// Afecta compras NUEVAS; las vigentes conservan su precio en <c>SubscriptionSeat.UnitPrice</c> (se copia
    /// al comprar). Molde: <c>AddOnDefinition.ReplacePriceTiers</c>, pero por-tipo para no tocar los demás.</summary>
    public Result SetTypePrices(SeatType seatType, Money monthly, Money yearly, Guid actorUserId, DateTime nowUtc)
    {
        Upsert(seatType, BillingCycle.Monthly, monthly);
        Upsert(seatType, BillingCycle.Yearly, yearly);
        Touch(actorUserId, nowUtc);
        return Result.Success();
    }

    private void Upsert(SeatType seatType, BillingCycle billingCycle, Money unitAmount)
    {
        var existing = _priceTiers.FirstOrDefault(tier =>
            tier.SeatType == seatType && tier.BillingCycle == billingCycle
        );

        if (existing is not null)
            existing.ChangeAmount(unitAmount);
        else
            _priceTiers.Add(SeatPriceTier.Create(Id, seatType, billingCycle, unitAmount).Value);
    }

    private void Touch(Guid actorUserId, DateTime nowUtc)
    {
        UpdatedAtUtc = nowUtc;
        UpdatedBy = actorUserId;
    }
}
