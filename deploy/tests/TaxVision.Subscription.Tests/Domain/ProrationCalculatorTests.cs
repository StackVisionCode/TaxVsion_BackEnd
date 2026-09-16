using TaxVision.Subscription.Domain.AddOns;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Tests.Domain;

public sealed class ProrationCalculatorTests
{
    private static readonly DateTime PeriodStart = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime PeriodEnd = new(2026, 1, 31, 0, 0, 0, DateTimeKind.Utc); // 30 días

    [Fact]
    public void InitialPeriod_charges_the_fraction_of_days_remaining()
    {
        var full = Money.Create(30m, "USD").Value;
        var chargeStart = new DateTime(2026, 1, 16, 0, 0, 0, DateTimeKind.Utc); // quedan 15 de 30 días

        var result = ProrationCalculator.InitialPeriod(full, PeriodStart, PeriodEnd, chargeStart);

        Assert.True(result.IsSuccess);
        Assert.Equal(15.00m, result.Value.Amount);
        Assert.Equal("USD", result.Value.Currency);
    }

    [Fact]
    public void InitialPeriod_charges_the_full_price_when_bought_at_the_period_start()
    {
        var full = Money.Create(30m, "USD").Value;

        var result = ProrationCalculator.InitialPeriod(full, PeriodStart, PeriodEnd, PeriodStart);

        Assert.True(result.IsSuccess);
        Assert.Equal(30m, result.Value.Amount);
    }

    [Fact]
    public void InitialPeriod_charges_zero_when_bought_at_or_after_the_period_end()
    {
        var full = Money.Create(30m, "USD").Value;

        var result = ProrationCalculator.InitialPeriod(full, PeriodStart, PeriodEnd, PeriodEnd);

        Assert.True(result.IsSuccess);
        Assert.Equal(0m, result.Value.Amount);
    }

    [Fact]
    public void InitialPeriod_fails_when_the_period_is_not_positive()
    {
        var full = Money.Create(30m, "USD").Value;

        var result = ProrationCalculator.InitialPeriod(full, PeriodEnd, PeriodStart, PeriodEnd);

        Assert.True(result.IsFailure);
        Assert.Equal("Proration.InvalidPeriod", result.Error.Code);
    }
}
