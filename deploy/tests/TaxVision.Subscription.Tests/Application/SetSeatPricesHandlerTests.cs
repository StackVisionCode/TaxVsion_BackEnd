using TaxVision.Subscription.Application.Seats.Commands.SetSeatPrices;
using TaxVision.Subscription.Domain.Seats;
using TaxVision.Subscription.Domain.ValueObjects;
using TaxVision.Subscription.Tests.TestDoubles;
using Xunit;

namespace TaxVision.Subscription.Tests.Application;

/// <summary>El PlatformAdmin edita el precio mensual/anual de un tipo de asiento en el catálogo global.</summary>
public sealed class SetSeatPricesHandlerTests
{
    private static SeatPricing CatalogWithStandard(decimal monthly, decimal yearly)
    {
        var pricing = SeatPricing.Seed(Guid.NewGuid(), DateTime.UtcNow);
        pricing.AddPriceTier(
            SeatPriceTier
                .Create(pricing.Id, SeatType.Standard, BillingCycle.Monthly, Money.Create(monthly, "USD").Value)
                .Value
        );
        pricing.AddPriceTier(
            SeatPriceTier
                .Create(pricing.Id, SeatType.Standard, BillingCycle.Yearly, Money.Create(yearly, "USD").Value)
                .Value
        );
        return pricing;
    }

    [Fact]
    public async Task Updates_the_price_and_saves()
    {
        var pricing = CatalogWithStandard(12m, 120m);
        var unitOfWork = new FakeUnitOfWork();

        var result = await SetSeatPricesHandler.Handle(
            new SetSeatPricesCommand("Standard", MonthlyUsd: 20m, YearlyUsd: 200m, ActorUserId: Guid.NewGuid()),
            new FakeSeatPricingRepository(pricing),
            unitOfWork,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);
        Assert.Equal(20m, pricing.ResolveUnitPrice(SeatType.Standard, BillingCycle.Monthly).Value.Amount);
        Assert.Equal(200m, pricing.ResolveUnitPrice(SeatType.Standard, BillingCycle.Yearly).Value.Amount);
    }

    [Fact]
    public async Task Fails_for_an_unknown_seat_type()
    {
        var unitOfWork = new FakeUnitOfWork();

        var result = await SetSeatPricesHandler.Handle(
            new SetSeatPricesCommand("Bogus", MonthlyUsd: 20m, YearlyUsd: 200m, ActorUserId: Guid.NewGuid()),
            new FakeSeatPricingRepository(CatalogWithStandard(12m, 120m)),
            unitOfWork,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("Seat.InvalidType", result.Error.Code);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Fails_when_the_catalog_does_not_exist()
    {
        var unitOfWork = new FakeUnitOfWork();

        var result = await SetSeatPricesHandler.Handle(
            new SetSeatPricesCommand("Standard", MonthlyUsd: 20m, YearlyUsd: 200m, ActorUserId: Guid.NewGuid()),
            new FakeSeatPricingRepository(pricing: null),
            unitOfWork,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("SeatPricing.NotFound", result.Error.Code);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }
}
