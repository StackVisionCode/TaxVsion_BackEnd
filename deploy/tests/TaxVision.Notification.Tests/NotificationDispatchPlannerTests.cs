using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;
using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Domain.Preferences;

namespace TaxVision.Notification.Tests;

public sealed class NotificationDispatchPlannerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    [Fact]
    public void Channel_not_allowed_by_the_catalog_is_not_planned()
    {
        // Solo InApp en el catálogo; el contenido de correo no se intenta.
        var intent = Intent(NotificationKinds.CloudStorageFilePendingReview) with
        {
            InApp = new InAppContent(),
            Email = new EmailContent("file.pending_review.v1", new Dictionary<string, object?>()),
        };

        var plan = NotificationDispatchPlanner.Plan(intent, Entry(intent.Kind));

        Assert.True(plan.For(NotificationChannel.InApp).Attempt);
        Assert.Equal(ChannelOutcome.NotAllowedByCatalog, plan.For(NotificationChannel.Email).SkippedAs);
        Assert.Equal(ChannelOutcome.NotRequested, plan.For(NotificationChannel.Sms).SkippedAs);
    }

    [Fact]
    public void Mandatory_channel_ignores_the_preference()
    {
        var intent = Intent(NotificationKinds.CloudStorageFileInfected) with
        {
            InApp = new InAppContent(),
            Push = new PushContent(),
        };

        var plan = NotificationDispatchPlanner.Plan(intent, Entry(intent.Kind));

        Assert.True(plan.For(NotificationChannel.InApp).IgnoresPreference);
        // El push nunca es obligatorio.
        Assert.False(plan.For(NotificationChannel.Push).IgnoresPreference);
    }

    [Fact]
    public void Unknown_kind_plans_nothing()
    {
        var intent = Intent("unknown.kind") with { InApp = new InAppContent(), Push = new PushContent() };

        var plan = NotificationDispatchPlanner.Plan(intent, entry: null);

        Assert.Empty(plan.Attempts);
        Assert.Equal(ChannelOutcome.Failed, plan.For(NotificationChannel.InApp).SkippedAs);
        Assert.Equal(ChannelOutcome.Failed, plan.For(NotificationChannel.Push).SkippedAs);
        Assert.Equal(ChannelOutcome.NotRequested, plan.For(NotificationChannel.Email).SkippedAs);
    }

    [Fact]
    public void Preference_category_comes_from_the_catalog()
    {
        var intent = Intent(NotificationKinds.RemindersDue) with { InApp = new InAppContent() };

        var plan = NotificationDispatchPlanner.Plan(intent, Entry(intent.Kind));

        Assert.Equal(NotificationCategory.Reminders, plan.PreferenceCategory);
    }

    private static NotificationCatalogEntry Entry(string kind) => NotificationCatalog.GetRequired(kind);

    private static NotificationIntent Intent(string kind) =>
        new(
            kind,
            TenantId,
            new NotificationTarget(UserId),
            Guid.NewGuid(),
            "test.event.v1",
            DateTime.UtcNow,
            CorrelationId: null
        );
}
