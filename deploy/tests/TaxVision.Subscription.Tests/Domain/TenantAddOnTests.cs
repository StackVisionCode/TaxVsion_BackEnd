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

    [Fact]
    public void CoTermTo_aligns_period_end_and_next_renewal_to_the_subscription()
    {
        var addOn = CreateAddOn();
        var subscriptionPeriodEndUtc = addOn.CurrentPeriodStartUtc.AddDays(93);

        var result = addOn.CoTermTo(subscriptionPeriodEndUtc, Guid.Empty, DateTime.UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal(subscriptionPeriodEndUtc, addOn.CurrentPeriodEndUtc);
        Assert.Equal(subscriptionPeriodEndUtc, addOn.NextRenewalAtUtc);
    }

    [Fact]
    public void CoTermTo_fails_when_the_target_is_not_after_the_period_start()
    {
        var addOn = CreateAddOn();

        var result = addOn.CoTermTo(addOn.CurrentPeriodStartUtc, Guid.Empty, DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal("AddOn.InvalidCoTerm", result.Error.Code);
    }

    [Fact]
    public void BeginInitialCharge_schedules_a_charge_for_the_current_period_without_advancing_it()
    {
        var addOn = CreateAddOn();
        var periodEndBefore = addOn.CurrentPeriodEndUtc;

        var result = addOn.BeginInitialCharge("addon-initial-1", Guid.Empty, DateTime.UtcNow);

        Assert.True(result.IsSuccess);
        var renewal = Assert.Single(addOn.Renewals);
        Assert.Equal(addOn.CurrentPeriodStartUtc, renewal.PeriodStartUtc);
        Assert.Equal(addOn.CurrentPeriodEndUtc, renewal.PeriodEndUtc);
        Assert.Equal(periodEndBefore, addOn.CurrentPeriodEndUtc); // no avanza el período
    }

    [Fact]
    public void BeginInitialCharge_is_idempotent_by_key()
    {
        var addOn = CreateAddOn();

        addOn.BeginInitialCharge("addon-initial-1", Guid.Empty, DateTime.UtcNow);
        var result = addOn.BeginInitialCharge("addon-initial-1", Guid.Empty, DateTime.UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Single(addOn.Renewals);
    }

    [Fact]
    public void SupersedeByPlan_cancels_with_the_given_reason()
    {
        var addOn = CreateAddOn();

        var result = addOn.SupersedeByPlan("Absorbed by plan upgrade", Guid.Empty, DateTime.UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Equal(AddOnStatus.Cancelled, addOn.Status);
        Assert.Equal("Absorbed by plan upgrade", addOn.CancellationReason);
    }

    [Fact]
    public void SupersedeByPlan_fails_from_a_terminal_status()
    {
        var addOn = CreateAddOn();
        addOn.CancelActive("user cancelled", Guid.Empty, DateTime.UtcNow);

        var result = addOn.SupersedeByPlan("Absorbed by plan upgrade", Guid.Empty, DateTime.UtcNow);

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
