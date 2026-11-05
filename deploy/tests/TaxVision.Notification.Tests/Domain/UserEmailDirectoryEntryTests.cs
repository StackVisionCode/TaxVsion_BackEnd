using TaxVision.Notification.Domain.Directory;

namespace TaxVision.Notification.Tests.Domain;

public sealed class UserEmailDirectoryEntryTests
{
    private static readonly DateTime T0 = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Older_deactivation_after_newer_reactivation_is_ignored()
    {
        var entry = UserEmailDirectoryEntry.Create(Guid.NewGuid(), Guid.NewGuid(), "user@example.test", true, T0);

        Assert.True(entry.SetActive(true, T0.AddMinutes(2)));
        var applied = entry.SetActive(false, T0.AddMinutes(1));

        Assert.False(applied);
        Assert.True(entry.IsActive);
        Assert.Equal(T0.AddMinutes(2), entry.LastSourceEventAtUtc);
    }

    [Fact]
    public void Older_email_update_does_not_clear_a_newer_stale_mark()
    {
        var entry = UserEmailDirectoryEntry.Create(Guid.NewGuid(), Guid.NewGuid(), "user@example.test", true, T0);

        Assert.True(entry.MarkStale(T0.AddMinutes(2)));
        Assert.False(entry.UpdateEmail("user@example.test", true, T0.AddMinutes(1)));

        Assert.True(entry.IsStale);
    }

    [Fact]
    public void Row_without_source_time_accepts_any_event()
    {
        var entry = UserEmailDirectoryEntry.Create(Guid.NewGuid(), Guid.NewGuid(), "user@example.test");

        Assert.True(entry.SetActive(false, T0));
        Assert.False(entry.IsActive);
        Assert.Equal(T0, entry.LastSourceEventAtUtc);
    }
}
