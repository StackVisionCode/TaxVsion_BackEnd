using TaxVision.Subscription.Domain.Settings;

namespace TaxVision.Subscription.Tests.Domain;

public sealed class SubscriptionTenantSettingsTests
{
    [Fact]
    public void Default_settings_allow_a_fourteen_day_trial()
    {
        var settings = SubscriptionTenantSettings.Default(Guid.NewGuid(), Guid.Empty, DateTime.UtcNow).Value;

        Assert.True(settings.AllowTrial);
        Assert.Equal(14, settings.TrialDays.Value);
        Assert.Equal([7, 3, 1], settings.NotifyBeforeRenewalDays);
    }

    [Fact]
    public void ApplyPatch_updates_only_the_touched_fields()
    {
        var settings = SubscriptionTenantSettings.Default(Guid.NewGuid(), Guid.Empty, DateTime.UtcNow).Value;

        var result = settings.ApplyPatch(
            new SubscriptionSettingsPatch(AllowSeatSelfAssignment: true), Guid.Empty, DateTime.UtcNow);

        Assert.True(result.IsSuccess);
        Assert.True(settings.AllowSeatSelfAssignment);
        Assert.True(settings.AllowAutoRenewTenantSubscription);
    }

    [Fact]
    public void ApplyPatch_rejects_max_seats_below_min_seats()
    {
        var settings = SubscriptionTenantSettings.Default(Guid.NewGuid(), Guid.Empty, DateTime.UtcNow).Value;

        var result = settings.ApplyPatch(
            new SubscriptionSettingsPatch(MinSeatsRequired: 5, MaxSeatsAllowed: 2), Guid.Empty, DateTime.UtcNow);

        Assert.True(result.IsFailure);
        Assert.Equal("SubscriptionSettings.InvalidMaxSeats", result.Error.Code);
    }

    [Fact]
    public void ApplyPatch_can_clear_max_seats_allowed()
    {
        var settings = SubscriptionTenantSettings.Default(Guid.NewGuid(), Guid.Empty, DateTime.UtcNow).Value;
        settings.ApplyPatch(new SubscriptionSettingsPatch(MaxSeatsAllowed: 10), Guid.Empty, DateTime.UtcNow);

        var result = settings.ApplyPatch(new SubscriptionSettingsPatch(ClearMaxSeatsAllowed: true), Guid.Empty, DateTime.UtcNow);

        Assert.True(result.IsSuccess);
        Assert.Null(settings.MaxSeatsAllowed);
    }
}
