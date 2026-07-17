using TaxVision.Subscription.Domain.AddOns;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Tests.Domain;

public sealed class TenantAddOnTests
{
    [Fact]
    public void Purchase_activates_immediately_with_a_period()
    {
        var addOn = CreateAddOn();

        Assert.Equal(AddOnStatus.Active, addOn.Status);
        Assert.True(addOn.CurrentPeriodEndUtc > addOn.CurrentPeriodStartUtc);
    }

    [Fact]
    public void Purchase_rejects_multiple_instances_when_not_allowed()
    {
        var definition = AddOnDefinition
            .Create(
                AddOnCode.Create("signature.premium").Value,
                "Premium signature",
                "desc",
                "signature",
                allowMultipleInstances: false,
                [BillingCycle.Monthly],
                Guid.Empty,
                DateTime.UtcNow
            )
            .Value;

        var result = TenantAddOn.Purchase(
            Guid.NewGuid(),
            definition,
            quantity: 2,
            Money.Zero("USD"),
            BillingCycle.Monthly,
            autoRenew: true,
            Guid.Empty,
            DateTime.UtcNow
        );

        Assert.True(result.IsFailure);
        Assert.Equal("AddOn.MultipleInstancesNotAllowed", result.Error.Code);
    }

    [Fact]
    public void CancelActive_transitions_to_cancelled()
    {
        var addOn = CreateAddOn();

        var result = addOn.CancelActive("no longer needed", Guid.Empty, DateTime.UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal(AddOnStatus.Cancelled, addOn.Status);
    }

    [Fact]
    public void ExpireAfterCancellationPeriodEnded_requires_cancelled_status()
    {
        var addOn = CreateAddOn();

        var result = addOn.ExpireAfterCancellationPeriodEnded(Guid.Empty, DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal("AddOn.InvalidTransition", result.Error.Code);
    }

    private static TenantAddOn CreateAddOn()
    {
        var definition = AddOnDefinition
            .Create(
                AddOnCode.Create("storage.extra_100gb").Value,
                "Extra storage",
                "desc",
                "storage",
                allowMultipleInstances: true,
                [BillingCycle.Monthly],
                Guid.Empty,
                DateTime.UtcNow
            )
            .Value;

        return TenantAddOn
            .Purchase(
                Guid.NewGuid(),
                definition,
                quantity: 1,
                Money.Zero("USD"),
                BillingCycle.Monthly,
                autoRenew: true,
                Guid.Empty,
                DateTime.UtcNow
            )
            .Value;
    }
}
