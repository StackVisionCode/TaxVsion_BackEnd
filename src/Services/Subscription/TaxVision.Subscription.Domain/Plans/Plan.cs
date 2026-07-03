using BuildingBlocks.Domain;
using BuildingBlocks.Results;
using TaxVision.Subscription.Domain.Modules;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Domain.Plans;

public sealed class Plan : BaseEntity
{
    public string Code { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public string Title { get; private set; } = string.Empty;
    public string Description { get; private set; } = default!;
    public decimal BasePriceMonthly { get; private set; }
    public decimal BasePriceAnnual { get; private set; }
    public decimal PricePerAdditionalSeat { get; private set; }
    public int IncludedSeats { get; private set; }
    public string Currency { get; private set; } = "USD";
    public bool IsActive { get; private set; }
    public ServiceLevel ServiceLevel { get; private set; } = ServiceLevel.Standard;
    public DateTime UpdatedAtUtc { get; private set; }

    private readonly List<PlanFeature> _features = [];
    public IReadOnlyList<PlanFeature> Features => _features.AsReadOnly();

    private readonly List<PlanModule> _planModules = [];
    public IReadOnlyList<PlanModule> PlanModules => _planModules.AsReadOnly();

    private Plan() { }

    public static Result<Plan> Create(
        string code, string name, string description,
        decimal basePriceMonthly, decimal basePriceAnnual,
        decimal pricePerAdditionalSeat, int includedSeats,
        string currency, IReadOnlyList<string> featureCodes,
        string? title = null,
        ServiceLevel serviceLevel = ServiceLevel.Standard)
    {
        if (string.IsNullOrWhiteSpace(code))
            return Result.Failure<Plan>(new Error("Plan.Code", "Code is required."));
        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure<Plan>(new Error("Plan.Name", "Name is required."));
        if (basePriceMonthly < 0 || basePriceAnnual < 0 || pricePerAdditionalSeat < 0)
            return Result.Failure<Plan>(new Error("Plan.Price", "Prices cannot be negative."));
        if (includedSeats < 0)
            return Result.Failure<Plan>(new Error("Plan.Seats", "IncludedSeats cannot be negative."));

        var plan = new Plan
        {
            Id = Guid.NewGuid(),
            Code = code.Trim().ToLowerInvariant(),
            Name = name.Trim(),
            Title = (title ?? name).Trim(),
            Description = description.Trim(),
            BasePriceMonthly = basePriceMonthly,
            BasePriceAnnual = basePriceAnnual,
            PricePerAdditionalSeat = pricePerAdditionalSeat,
            IncludedSeats = includedSeats,
            Currency = currency,
            IsActive = true,
            ServiceLevel = serviceLevel,
            UpdatedAtUtc = DateTime.UtcNow
        };
        plan._features.AddRange(featureCodes.Select(f => new PlanFeature
        {
            PlanId = plan.Id,
            FeatureCode = f
        }));
        return Result.Success(plan);
    }

    public Result Update(string name, string title, string description, bool isActive, ServiceLevel serviceLevel)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure(new Error("Plan.Name", "Name is required."));
        Name = name.Trim();
        Title = title.Trim();
        Description = description.Trim();
        IsActive = isActive;
        ServiceLevel = serviceLevel;
        UpdatedAtUtc = DateTime.UtcNow;
        return Result.Success();
    }

    public Result Activate()
    {
        IsActive = true;
        UpdatedAtUtc = DateTime.UtcNow;
        return Result.Success();
    }

    public Result Deactivate()
    {
        IsActive = false;
        UpdatedAtUtc = DateTime.UtcNow;
        return Result.Success();
    }

    /// <summary>
    /// El dueño actualiza precios. El cambio aplica en la PRÓXIMA renovación de cada suscriptor.
    /// </summary>
    public Result UpdatePricing(
        decimal basePriceMonthly, decimal basePriceAnnual,
        decimal pricePerAdditionalSeat)
    {
        if (basePriceMonthly < 0 || basePriceAnnual < 0 || pricePerAdditionalSeat < 0)
            return Result.Failure(new Error("Plan.Price", "Prices cannot be negative."));

        BasePriceMonthly = basePriceMonthly;
        BasePriceAnnual = basePriceAnnual;
        PricePerAdditionalSeat = pricePerAdditionalSeat;
        UpdatedAtUtc = DateTime.UtcNow;
        return Result.Success();
    }

    public Result UpdateSeats(int includedSeats)
    {
        if (includedSeats < 0)
            return Result.Failure(new Error("Plan.Seats", "IncludedSeats cannot be negative."));
        IncludedSeats = includedSeats;
        UpdatedAtUtc = DateTime.UtcNow;
        return Result.Success();
    }

    public decimal GetBasePrice(BillingPeriod period) => period switch
    {
        BillingPeriod.Annual => BasePriceAnnual,
        _ => BasePriceMonthly
    };

    /// <summary>
    /// Returns the price for the given billing period, matching CloudTax GetPriceForPeriod logic.
    /// </summary>
    public decimal GetPriceForPeriod(BillingPeriod period) => period switch
    {
        BillingPeriod.Annual => BasePriceAnnual > 0 ? BasePriceAnnual : BasePriceMonthly * 12,
        _ => BasePriceMonthly
    };
}
