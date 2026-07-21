using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Domain.Preferences;

namespace TaxVision.Notification.Tests.Domain;

public sealed class UserNotificationPreferenceTests
{
    [Fact]
    public void Create_fails_for_a_locked_category()
    {
        var result = UserNotificationPreference.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            NotificationCategory.AccountSecurity,
            NotificationChannel.Email,
            false
        );

        Assert.True(result.IsFailure);
        Assert.Equal("UserNotificationPreference.CategoryLocked", result.Error.Code);
    }

    [Fact]
    public void Create_succeeds_for_an_unlocked_category()
    {
        var result = UserNotificationPreference.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            NotificationCategory.StorageAndQuota,
            NotificationChannel.Email,
            false
        );

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.Enabled);
    }

    [Fact]
    public void SetEnabled_is_idempotent_when_value_does_not_change()
    {
        var preference = UserNotificationPreference
            .Create(Guid.NewGuid(), Guid.NewGuid(), NotificationCategory.Collaboration, NotificationChannel.InApp, true)
            .Value;

        preference.SetEnabled(false);
        var firstUpdate = preference.UpdatedAtUtc;
        preference.SetEnabled(false);

        Assert.False(preference.Enabled);
        Assert.Equal(firstUpdate, preference.UpdatedAtUtc);
    }

    [Theory]
    [InlineData(NotificationCategory.AccountSecurity, true)]
    [InlineData(NotificationCategory.DocumentsAndSignatures, false)]
    [InlineData(NotificationCategory.StorageAndQuota, false)]
    [InlineData(NotificationCategory.Billing, false)]
    [InlineData(NotificationCategory.Collaboration, false)]
    public void Only_account_security_is_locked(NotificationCategory category, bool expectedLocked)
    {
        Assert.Equal(expectedLocked, NotificationCategoryRules.IsLocked(category));
    }
}
