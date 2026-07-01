using TaxVision.Subscription.Domain.Plans;

namespace TaxVision.Subscription.Application.Plans.Dtos;

public sealed class PlanDto
{
    public Guid Id { get; init; }
    public string Name { get; init; } = default!;
    public string Title { get; init; } = default!;
    public string Description { get; init; } = default!;
    public List<string> Features { get; init; } = [];
    public decimal BasePriceMonthly { get; init; }
    public decimal BasePriceAnnual { get; init; }
    public decimal PricePerAdditionalSeat { get; init; }
    public int IncludedSeats { get; init; }
    public string Currency { get; init; } = "USD";
    public bool IsActive { get; init; }
    public ServiceLevel ServiceLevel { get; init; }
    public List<Guid> ModuleIds { get; init; } = [];
    public List<string> ModuleNames { get; init; } = [];
    public DateTime UpdatedAtUtc { get; init; }

    // Convenience
    public decimal EffectiveAnnualPrice => BasePriceAnnual > 0 ? BasePriceAnnual : BasePriceMonthly * 12;
    public decimal? AnnualDiscountPercentage =>
        BasePriceAnnual > 0 && BasePriceMonthly > 0
            ? Math.Round((1 - BasePriceAnnual / (BasePriceMonthly * 12)) * 100, 2)
            : null;
}
