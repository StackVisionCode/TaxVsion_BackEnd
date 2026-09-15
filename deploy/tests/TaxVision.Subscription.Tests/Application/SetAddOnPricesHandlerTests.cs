using TaxVision.Subscription.Application.AddOns.Commands.SetAddOnPrices;
using TaxVision.Subscription.Domain.AddOns;
using TaxVision.Subscription.Domain.ValueObjects;
using TaxVision.Subscription.Tests.TestDoubles;
using Xunit;

namespace TaxVision.Subscription.Tests.Application;

public sealed class SetAddOnPricesHandlerTests
{
    [Fact]
    public async Task Replaces_the_price_tiers_of_an_existing_add_on()
    {
        var definition = PublishedAddOn(monthly: 29m, yearly: 290m);
        var addOns = new FakeAddOnDefinitionRepository(definition);
        var unitOfWork = new FakeUnitOfWork();

        var result = await SetAddOnPricesHandler.Handle(
            new SetAddOnPricesCommand(definition.Id, 39m, 390m, Guid.NewGuid()),
            addOns,
            unitOfWork,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);

        var monthly = Assert.Single(definition.PriceTiers, t => t.BillingCycle == BillingCycle.Monthly);
        var yearly = Assert.Single(definition.PriceTiers, t => t.BillingCycle == BillingCycle.Yearly);
        Assert.Equal(39m, monthly.UnitAmount.Amount);
        Assert.Equal(390m, yearly.UnitAmount.Amount);
    }

    [Fact]
    public async Task Fails_when_the_add_on_does_not_exist()
    {
        var addOns = new FakeAddOnDefinitionRepository();
        var unitOfWork = new FakeUnitOfWork();

        var result = await SetAddOnPricesHandler.Handle(
            new SetAddOnPricesCommand(Guid.NewGuid(), 39m, 390m, Guid.NewGuid()),
            addOns,
            unitOfWork,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("AddOn.NotFound", result.Error.Code);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    private static AddOnDefinition PublishedAddOn(decimal monthly, decimal yearly)
    {
        var nowUtc = DateTime.UtcNow;
        var definition = AddOnDefinition
            .Create(
                AddOnCode.Create("email.addon").Value,
                "Email",
                "Email",
                "module",
                false,
                [BillingCycle.Monthly, BillingCycle.Yearly],
                Guid.Empty,
                nowUtc
            )
            .Value;
        definition.AddPriceTier(
            AddOnPriceTier
                .Create(definition.Id, BillingCycle.Monthly, 1, null, Money.Create(monthly, "USD").Value)
                .Value
        );
        definition.AddPriceTier(
            AddOnPriceTier.Create(definition.Id, BillingCycle.Yearly, 1, null, Money.Create(yearly, "USD").Value).Value
        );
        definition.Publish(Guid.Empty, nowUtc);
        return definition;
    }
}
