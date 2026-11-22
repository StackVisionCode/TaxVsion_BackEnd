using TaxVision.Notification.Application.Consumers.Communication;

namespace TaxVision.Notification.Tests;

/// <summary>La zona de una reunión se escribe con el rótulo legible del kit, nunca el id ni "UTC" a mano.</summary>
public sealed class MeetingNotificationsTests
{
    [Fact]
    public void Zone_label_is_readable()
    {
        var label = MeetingNotifications.TimeZoneLabel(
            "America/New_York",
            new DateTime(2026, 12, 1, 15, 0, 0, DateTimeKind.Utc)
        );

        Assert.Equal("New York time, GMT-5", label);
    }

    [Fact]
    public void Without_zone_the_kit_label_is_used()
    {
        Assert.Equal("UTC", MeetingNotifications.TimeZoneLabel(null, DateTime.UtcNow));
    }
}
