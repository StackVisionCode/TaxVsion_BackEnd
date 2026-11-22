using BuildingBlocks.Results;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;
using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Domain.Preferences;
using TaxVision.Notification.Tests.TestDoubles;

namespace TaxVision.Notification.Tests;

public sealed class NotificationDispatcherDispatchTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    [Fact]
    public async Task Email_to_a_user_always_passes_through_the_preference_gate()
    {
        var preferences = new OffPreferences((NotificationCategory.Reminders, NotificationChannel.Email));
        var gateway = new GatedEmailGateway(preferences);
        var harness = new NotificationDispatcherHarness(
            preferences: preferences,
            emailGateway: gateway,
            scribe: new StaticScribe()
        );

        var summary = await harness.Dispatcher.DispatchAsync(EmailIntent());

        Assert.Empty(gateway.Queued);
        Assert.Equal(ChannelOutcome.SuppressedByPreference, summary.Channels[NotificationChannel.Email]);
    }

    [Fact]
    public async Task Email_to_a_user_with_the_preference_on_is_queued_with_user_and_category()
    {
        var preferences = new OffPreferences();
        var gateway = new GatedEmailGateway(preferences);
        var harness = new NotificationDispatcherHarness(
            preferences: preferences,
            emailGateway: gateway,
            scribe: new StaticScribe()
        );

        var summary = await harness.Dispatcher.DispatchAsync(EmailIntent());

        var queued = Assert.Single(gateway.Queued);
        Assert.Equal(UserId, queued.RecipientUserId);
        Assert.Equal(NotificationCategory.Reminders, queued.Category);
        Assert.Equal(NotificationKinds.RemindersDue, queued.NotificationKind);
        Assert.Equal(ChannelOutcome.Queued, summary.Channels[NotificationChannel.Email]);
    }

    [Fact]
    public async Task Channel_outside_the_catalog_is_not_sent()
    {
        var harness = new NotificationDispatcherHarness();
        var intent = EmailIntent() with
        {
            Kind = NotificationKinds.CloudStorageFilePendingReview,
            Email = new EmailContent("x.v1", new Dictionary<string, object?>()),
        };

        var summary = await harness.Dispatcher.DispatchAsync(intent);

        Assert.Equal(ChannelOutcome.NotAllowedByCatalog, summary.Channels[NotificationChannel.Email]);
    }

    [Fact]
    public async Task In_app_is_recorded_and_published_with_the_catalog_category()
    {
        var harness = new NotificationDispatcherHarness();
        var intent = EmailIntent() with
        {
            Email = null,
            InApp = new InAppContent(Params: new Dictionary<string, string> { ["title"] = "Call the bank" }),
        };

        var summary = await harness.Dispatcher.DispatchAsync(intent);

        Assert.Equal(ChannelOutcome.Sent, summary.Channels[NotificationChannel.InApp]);
        var published = Assert.Single(harness.Publisher.Published);
        var evt =
            Assert.IsType<BuildingBlocks.Messaging.NotificationIntegrationEvents.InAppNotificationRequestedIntegrationEvent>(
                published
            );
        Assert.Equal(NotificationKinds.RemindersDue, evt.Kind);
        Assert.Equal(UserId, evt.RecipientUserId);
    }

    private static NotificationIntent EmailIntent() =>
        new(
            NotificationKinds.RemindersDue,
            TenantId,
            new NotificationTarget(UserId, Email: "staff@example.com"),
            Guid.NewGuid(),
            "reminders.due.v1",
            DateTime.UtcNow,
            CorrelationId: null,
            Email: new EmailContent("reminder.due.v1", new Dictionary<string, object?> { ["title"] = "Call" })
        );

    private sealed class OffPreferences(params (NotificationCategory, NotificationChannel)[] off)
        : IUserNotificationPreferenceRepository
    {
        public Task<bool> IsEnabledAsync(
            Guid tenantId,
            Guid userId,
            NotificationCategory category,
            NotificationChannel channel,
            CancellationToken ct = default
        ) => Task.FromResult(!off.Contains((category, channel)));

        public Task<UserNotificationPreference?> GetAsync(
            Guid tenantId,
            Guid userId,
            NotificationCategory category,
            NotificationChannel channel,
            CancellationToken ct = default
        ) => Task.FromResult<UserNotificationPreference?>(null);

        public Task<IReadOnlyList<UserNotificationPreference>> ListForUserAsync(
            Guid tenantId,
            Guid userId,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<UserNotificationPreference>>([]);

        public Task AddAsync(UserNotificationPreference preference, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private sealed class StaticScribe : IScribeRenderClient
    {
        public Task<Result<ScribeRenderedEmail>> RenderAsync(
            string eventKey,
            Guid tenantId,
            IReadOnlyDictionary<string, object?> variables,
            CancellationToken ct = default
        ) => Task.FromResult(Result.Success(new ScribeRenderedEmail("Subject", "<p>Body</p>", "Body")));

        public Task<Result<ScribeRenderedEmail>> RenderAsync(
            string eventKey,
            Guid tenantId,
            IReadOnlyDictionary<string, object?> variables,
            ScribeRenderAudience audience,
            CancellationToken ct = default
        ) => RenderAsync(eventKey, tenantId, variables, ct);

        public Task<Result<ScribeRenderedText>> RenderTextAsync(
            string eventKey,
            Guid tenantId,
            IReadOnlyDictionary<string, object?> variables,
            CancellationToken ct = default
        ) => ScribeTestDefaults.TextUnsupported();
    }
}
