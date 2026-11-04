using BuildingBlocks.Messaging.NotificationIntegrationEvents;
using BuildingBlocks.Results;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;
using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Domain.Preferences;
using TaxVision.Notification.Tests.TestDoubles;

namespace TaxVision.Notification.Tests;

/// <summary>
/// El dispatcher no entrega push ni in-app a un usuario suspendido o retirado según el directorio
/// local, aunque el consumer no lo compruebe. Sin fila en el directorio no hay señal y se entrega.
/// </summary>
public sealed class NotificationDispatcherInactiveRecipientTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    [Fact]
    public async Task Inactive_recipient_gets_no_push()
    {
        var (harness, pushSender) = Build(isActive: false);

        await harness.Dispatcher.SendPushAsync(
            TenantId,
            UserId,
            "Title",
            "Body",
            NotificationCategory.Reminders,
            "reminder.due",
            Guid.NewGuid(),
            "corr"
        );

        Assert.Empty(pushSender.Sent);
        Assert.False(
            await harness.Dispatcher.CanSendPushAsync(TenantId, UserId, NotificationCategory.Reminders, "reminder.due")
        );
    }

    [Fact]
    public async Task Inactive_recipient_gets_no_in_app_notification()
    {
        var (harness, _) = Build(isActive: false);

        await RecordInAppAsync(harness);

        Assert.Empty(harness.Publisher.Published);
    }

    [Fact]
    public async Task Active_or_unknown_recipient_is_delivered()
    {
        var (active, activePush) = Build(isActive: true);
        var (unknown, unknownPush) = Build(isActive: null);

        foreach (var harness in new[] { active, unknown })
        {
            await harness.Dispatcher.SendPushAsync(
                TenantId,
                UserId,
                "Title",
                "Body",
                NotificationCategory.Reminders,
                "reminder.due",
                Guid.NewGuid(),
                "corr"
            );
            await RecordInAppAsync(harness);
            Assert.Single(harness.Publisher.Published.OfType<InAppNotificationRequestedIntegrationEvent>());
        }

        Assert.Single(activePush.Sent);
        Assert.Single(unknownPush.Sent);
    }

    private static Task RecordInAppAsync(NotificationDispatcherHarness harness) =>
        harness.Dispatcher.RecordInAppAsync(
            TenantId,
            $"user:{UserId:N}",
            "Title",
            NotificationCategory.Reminders,
            "reminder.due",
            Guid.NewGuid(),
            "corr",
            recipientUserId: UserId,
            inApp: new InAppNotificationRequest(
                NotificationKinds.RemindersDue,
                UserId,
                Guid.NewGuid(),
                "reminder.due.v1",
                DateTime.UtcNow,
                "Title"
            )
        );

    private static (NotificationDispatcherHarness Harness, RecordingPushSender PushSender) Build(bool? isActive)
    {
        var directory = new InMemoryUserEmailDirectoryRepository();
        if (isActive is { } active)
            directory.Seed(TenantId, UserId, "staff@example.com", active);

        var pushSender = new RecordingPushSender();
        var harness = new NotificationDispatcherHarness(
            pushSender: pushSender,
            pushDeviceTokens: new SingleDevice(),
            userDirectory: directory
        );
        return (harness, pushSender);
    }

    private sealed class RecordingPushSender : IPushSender
    {
        public List<PushMessage> Sent { get; } = [];

        public Task<Result> SendAsync(PushMessage message, CancellationToken ct = default)
        {
            Sent.Add(message);
            return Task.FromResult(Result.Success());
        }
    }

    private sealed class SingleDevice : IPushDeviceTokenRepository
    {
        private readonly PushDeviceToken _token = PushDeviceToken
            .Register(TenantId, UserId, PushPlatform.Fcm, "token-1", deviceId: null)
            .Value;

        public Task AddAsync(PushDeviceToken token, CancellationToken ct = default) => Task.CompletedTask;

        public Task<PushDeviceToken?> FindByTokenAsync(Guid tenantId, string token, CancellationToken ct = default) =>
            Task.FromResult<PushDeviceToken?>(null);

        public Task<PushDeviceToken?> GetAsync(Guid tenantId, Guid id, CancellationToken ct = default) =>
            Task.FromResult<PushDeviceToken?>(null);

        public Task<IReadOnlyList<PushDeviceToken>> ListActiveForUserAsync(
            Guid tenantId,
            Guid userId,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<PushDeviceToken>>([_token]);

        public Task RevokeAsync(Guid tenantId, Guid id, CancellationToken ct = default) => Task.CompletedTask;
    }
}
