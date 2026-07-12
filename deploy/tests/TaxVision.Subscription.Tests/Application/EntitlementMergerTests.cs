using TaxVision.Subscription.Application.Entitlements;
using TaxVision.Subscription.Domain.AddOns;
using TaxVision.Subscription.Domain.Entitlements;
using TaxVision.Subscription.Domain.ValueObjects;

namespace TaxVision.Subscription.Tests.Application;

public sealed class EntitlementMergerTests
{
    [Fact]
    public void MergeAddOnValue_with_no_existing_entry_creates_one_from_the_addon()
    {
        var key = EntitlementKey.Create("signature.enabled").Value;

        var merged = EntitlementMerger.MergeAddOnValue(null, key, EntitlementValueType.Bool, "true", AddOnMergeStrategy.Or);

        Assert.Equal("true", merged.Value);
        Assert.Equal(EntitlementSource.AddOn, merged.Source);
    }

    [Fact]
    public void MergeAddOnValue_or_strategy_combines_booleans()
    {
        var key = EntitlementKey.Create("signature.enabled").Value;
        var existing = new EntitlementEntry(key, EntitlementValueType.Bool, "false", EntitlementStatus.Active, EntitlementSource.Plan, null);

        var merged = EntitlementMerger.MergeAddOnValue(existing, key, EntitlementValueType.Bool, "true", AddOnMergeStrategy.Or);

        Assert.Equal("True", merged.Value);
    }

    [Fact]
    public void MergeAddOnValue_max_strategy_takes_the_larger_int()
    {
        var key = EntitlementKey.Create("seats.max").Value;
        var existing = new EntitlementEntry(key, EntitlementValueType.Int, "5", EntitlementStatus.Active, EntitlementSource.Plan, null);

        var merged = EntitlementMerger.MergeAddOnValue(existing, key, EntitlementValueType.Int, "3", AddOnMergeStrategy.Max);

        Assert.Equal("5", merged.Value);
    }

    [Fact]
    public void MergeAddOnValue_sum_strategy_adds_longs()
    {
        var key = EntitlementKey.Create("storage.max_bytes").Value;
        var existing = new EntitlementEntry(key, EntitlementValueType.Long, "1000", EntitlementStatus.Active, EntitlementSource.Plan, null);

        var merged = EntitlementMerger.MergeAddOnValue(existing, key, EntitlementValueType.Long, "500", AddOnMergeStrategy.Sum);

        Assert.Equal("1500", merged.Value);
    }

    [Fact]
    public void MergeAddOnValue_replace_strategy_overwrites_the_existing_value()
    {
        var key = EntitlementKey.Create("plan.tier").Value;
        var existing = new EntitlementEntry(key, EntitlementValueType.String, "standard", EntitlementStatus.Active, EntitlementSource.Plan, null);

        var merged = EntitlementMerger.MergeAddOnValue(existing, key, EntitlementValueType.String, "premium", AddOnMergeStrategy.Replace);

        Assert.Equal("premium", merged.Value);
    }
}
