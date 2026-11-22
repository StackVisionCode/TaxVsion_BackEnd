using TaxVision.Notification.Domain.Catalog;
using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Domain.Preferences;

namespace TaxVision.Notification.Application.Common;

/// <param name="SkippedAs">Null si el canal se intenta.</param>
public sealed record ChannelPlan(NotificationChannel Channel, ChannelOutcome? SkippedAs, bool IgnoresPreference)
{
    public bool Attempt => SkippedAs is null;
}

/// <param name="PreferenceCategory">Null si el kind no está en el catálogo.</param>
public sealed record DispatchPlan(NotificationCategory? PreferenceCategory, IReadOnlyList<ChannelPlan> Channels)
{
    public IEnumerable<ChannelPlan> Attempts => Channels.Where(channel => channel.Attempt);

    public ChannelPlan For(NotificationChannel channel) => Channels.Single(plan => plan.Channel == channel);
}

/// <summary>Función pura: intención + entrada del catálogo → qué canales se intentan y por qué no los demás.</summary>
public static class NotificationDispatchPlanner
{
    public static DispatchPlan Plan(NotificationIntent intent, NotificationCatalogEntry? entry)
    {
        var channels = new List<ChannelPlan>(4);
        foreach (var channel in Enum.GetValues<NotificationChannel>())
        {
            ChannelOutcome? skipped =
                !HasContent(intent, channel) ? ChannelOutcome.NotRequested
                : entry is null ? ChannelOutcome.Failed
                : !entry.AllowedChannels.Contains(channel) ? ChannelOutcome.NotAllowedByCatalog
                : null;
            channels.Add(new ChannelPlan(channel, skipped, skipped is null && entry!.IsMandatoryFor(channel)));
        }
        return new DispatchPlan(entry?.PreferenceCategory, channels);
    }

    private static bool HasContent(NotificationIntent intent, NotificationChannel channel) =>
        channel switch
        {
            NotificationChannel.InApp => intent.InApp is not null,
            NotificationChannel.Push => intent.Push is not null,
            NotificationChannel.Email => intent.Email is not null,
            NotificationChannel.Sms => intent.Sms is not null,
            _ => false,
        };
}
