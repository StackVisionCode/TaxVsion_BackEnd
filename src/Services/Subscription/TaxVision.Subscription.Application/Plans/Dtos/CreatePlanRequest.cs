using TaxVision.Subscription.Domain.Plans;

namespace TaxVision.Subscription.Application.Plans.Dtos;

public sealed class CreatePlanRequest
{
    public required string Name { get; init; }
    public required string Title { get; init; }
    public required string Description { get; init; }
    public required List<string> Features { get; init; }
    public required decimal BasePriceMonthly { get; init; }
    public decimal BasePriceAnnual { get; init; }
    public decimal PricePerAdditionalSeat { get; init; }
    public required int IncludedSeats { get; init; }
    public string Currency { get; init; } = "USD";
    public bool IsActive { get; init; } = true;
    public ServiceLevel ServiceLevel { get; init; } = ServiceLevel.Standard;
}
