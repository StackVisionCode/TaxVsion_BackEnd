using TaxVision.Subscription.Application.AddOns.Commands.CreateModuleAddOn;
using TaxVision.Subscription.Domain.AddOns;
using TaxVision.Subscription.Domain.ValueObjects;
using TaxVision.Subscription.Tests.TestDoubles;
using Xunit;

namespace TaxVision.Subscription.Tests.Application;

public sealed class CreateModuleAddOnHandlerTests
{
    [Fact]
    public async Task Creates_a_published_module_add_on_with_monthly_and_yearly_tiers()
    {
        var addOns = new FakeAddOnDefinitionRepository();
        var unitOfWork = new FakeUnitOfWork();
        var actor = Guid.NewGuid();

        var result = await CreateModuleAddOnHandler.Handle(
            new CreateModuleAddOnCommand("email.addon", "Email", "email", 29m, 290m, actor),
            addOns,
            unitOfWork,
            CancellationToken.None
        );

        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        Assert.NotNull(addOns.Added);
        Assert.Equal(result.Value, addOns.Added!.Id);
        Assert.Equal(AddOnDefinitionStatus.Published, addOns.Added.Status);
        Assert.Equal(1, unitOfWork.SaveChangesCallCount);

        var feature = Assert.Single(addOns.Added.Features);
        Assert.Equal("module.email", feature.FeatureKey.Value);
        Assert.True(feature.Enabled);

        var monthly = Assert.Single(addOns.Added.PriceTiers, t => t.BillingCycle == BillingCycle.Monthly);
        var yearly = Assert.Single(addOns.Added.PriceTiers, t => t.BillingCycle == BillingCycle.Yearly);
        Assert.Equal(29m, monthly.UnitAmount.Amount);
        Assert.Equal(290m, yearly.UnitAmount.Amount);
    }

    [Fact]
    public async Task Fails_when_an_add_on_with_the_same_code_already_exists()
    {
        var existing = AddOnDefinition
            .Create(
                AddOnCode.Create("email.addon").Value,
                "Email",
                "Email",
                "module",
                false,
                [BillingCycle.Monthly],
                Guid.Empty,
                DateTime.UtcNow
            )
            .Value;
        var addOns = new FakeAddOnDefinitionRepository(existing);
        var unitOfWork = new FakeUnitOfWork();

        var result = await CreateModuleAddOnHandler.Handle(
            new CreateModuleAddOnCommand("email.addon", "Email", "email", 29m, 290m, Guid.NewGuid()),
            addOns,
            unitOfWork,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("AddOn.CodeExists", result.Error.Code);
        Assert.Null(addOns.Added);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }

    [Fact]
    public async Task Fails_without_persisting_when_the_code_is_invalid()
    {
        var addOns = new FakeAddOnDefinitionRepository();
        var unitOfWork = new FakeUnitOfWork();

        var result = await CreateModuleAddOnHandler.Handle(
            new CreateModuleAddOnCommand("X", "Email", "email", 29m, 290m, Guid.NewGuid()),
            addOns,
            unitOfWork,
            CancellationToken.None
        );

        Assert.True(result.IsFailure);
        Assert.Equal("AddOnCode.Invalid", result.Error.Code);
        Assert.Null(addOns.Added);
        Assert.Equal(0, unitOfWork.SaveChangesCallCount);
    }
}
