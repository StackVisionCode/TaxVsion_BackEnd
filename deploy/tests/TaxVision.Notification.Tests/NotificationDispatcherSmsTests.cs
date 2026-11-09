using BuildingBlocks.Results;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Domain.Preferences;
using TaxVision.Notification.Tests.TestDoubles;

namespace TaxVision.Notification.Tests;

/// <summary>Canal SMS del dispatcher: gate, registro del log y token de idempotencia.</summary>
public sealed class NotificationDispatcherSmsTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    [Fact]
    public async Task A_sent_sms_is_logged_as_Sent()
    {
        var (harness, sms, logs, metrics) = Build();

        await Send(harness, recipientUserId: UserId);

        Assert.Equal("+15550001111", sms.Phone);
        Assert.Equal("Your code", sms.Text);
        Assert.Equal("auth.otp", sms.TemplateKey);
        var log = Assert.Single(logs.Logs);
        Assert.Equal(NotificationChannel.Sms, log.Channel);
        Assert.Equal(NotificationStatus.Sent, log.Status);
        Assert.Equal("+15550001111", log.Recipient);
        Assert.Equal(["sent:auth.otp:Sms"], metrics.Records);
    }

    [Fact]
    public async Task A_failed_sms_is_logged_as_Failed_and_does_not_throw()
    {
        var (harness, _, logs, metrics) = Build(new Error("Sms.Down", "provider down"));

        await Send(harness, recipientUserId: UserId);

        var log = Assert.Single(logs.Logs);
        Assert.Equal(NotificationStatus.Failed, log.Status);
        Assert.Equal("provider down", log.Error);
        Assert.Equal(["failed:auth.otp:Sms"], metrics.Records);
    }

    [Fact]
    public async Task The_explicit_token_wins()
    {
        var (harness, sms, _, _) = Build();

        await Send(harness, recipientUserId: UserId, relatedEventId: Guid.NewGuid(), token: "explicit");

        Assert.Equal("explicit", sms.Token);
    }

    [Fact]
    public async Task Without_a_token_the_event_id_is_used()
    {
        var (harness, sms, _, _) = Build();
        var eventId = Guid.NewGuid();

        await Send(harness, recipientUserId: UserId, relatedEventId: eventId);

        Assert.Equal(eventId.ToString("N"), sms.Token);
    }

    [Fact]
    public async Task Without_token_or_event_the_template_and_user_are_used()
    {
        var (harness, sms, _, _) = Build();

        await Send(harness, recipientUserId: UserId);

        Assert.Equal($"auth.otp:{UserId:N}", sms.Token);
    }

    [Fact]
    public async Task Without_token_event_or_user_there_is_no_token()
    {
        var (harness, sms, _, _) = Build();

        await Send(harness);

        Assert.Null(sms.Token);
    }

    [Fact]
    public async Task An_inactive_recipient_gets_no_sms_and_no_log()
    {
        var directory = new InMemoryUserEmailDirectoryRepository();
        directory.Seed(TenantId, UserId, "inactive@test.com", isActive: false);
        var (harness, sms, logs, metrics) = Build(directory: directory);

        await Send(harness, recipientUserId: UserId);

        Assert.Null(sms.Phone);
        Assert.Empty(logs.Logs);
        Assert.Equal(["none:auth.otp:Sms"], metrics.Records);
    }

    [Fact]
    public async Task A_disabled_preference_suppresses_the_sms()
    {
        var (harness, sms, logs, metrics) = Build(preferences: new DisabledPreferences());

        await Send(harness, recipientUserId: UserId);

        Assert.Null(sms.Phone);
        Assert.Empty(logs.Logs);
        Assert.Equal(["suppressed:auth.otp:Sms"], metrics.Records);
    }

    private static Task Send(
        NotificationDispatcherHarness harness,
        Guid? recipientUserId = null,
        Guid? relatedEventId = null,
        string? token = null
    ) =>
        harness.Dispatcher.SendSmsAsync(
            TenantId,
            "+15550001111",
            "Your code",
            NotificationCategory.Reminders,
            "auth.otp",
            relatedEventId,
            "corr",
            recipientUserId,
            token
        );

    private static (
        NotificationDispatcherHarness Harness,
        RecordingSms Sms,
        MemoryLogs Logs,
        RecordingDispatchMetrics Metrics
    ) Build(
        Error? failure = null,
        IUserEmailDirectoryRepository? directory = null,
        IUserNotificationPreferenceRepository? preferences = null
    )
    {
        var sms = new RecordingSms(failure);
        var logs = new MemoryLogs();
        var metrics = new RecordingDispatchMetrics();
        var harness = new NotificationDispatcherHarness(
            logs: logs,
            preferences: preferences,
            metrics: metrics,
            userDirectory: directory,
            smsSender: sms
        );
        return (harness, sms, logs, metrics);
    }

    private sealed class RecordingSms(Error? failure) : ISmsSender
    {
        public string? Phone { get; private set; }
        public string? Text { get; private set; }
        public string? TemplateKey { get; private set; }
        public string? Token { get; private set; }

        public Task<Result> SendAsync(
            string phoneNumber,
            string text,
            CancellationToken ct,
            string? templateKey,
            string? idempotencyToken
        )
        {
            (Phone, Text, TemplateKey, Token) = (phoneNumber, text, templateKey, idempotencyToken);
            return Task.FromResult(failure is null ? Result.Success() : Result.Failure(failure));
        }
    }

    private sealed class MemoryLogs : INotificationLogRepository
    {
        public List<NotificationLog> Logs { get; } = [];

        public Task<NotificationLog?> GetByRelatedEventAsync(
            Guid tenantId,
            Guid relatedEventId,
            string kind,
            NotificationChannel channel,
            string recipient,
            CancellationToken ct = default
        ) => Task.FromResult<NotificationLog?>(null);

        public Task AddAsync(NotificationLog log, CancellationToken ct = default)
        {
            Logs.Add(log);
            return Task.CompletedTask;
        }

        public Task<(IReadOnlyList<NotificationLog> Items, int TotalCount)> GetPagedAsync(
            Guid tenantId,
            NotificationStatus? status,
            int page,
            int size,
            CancellationToken ct = default
        ) => Task.FromResult<(IReadOnlyList<NotificationLog>, int)>((Logs, Logs.Count));

        public Task<NotificationLog?> GetByRelatedEventIdAsync(
            Guid tenantId,
            Guid relatedEventId,
            string templateKey,
            string recipient,
            CancellationToken ct = default
        ) => Task.FromResult<NotificationLog?>(null);
    }

    private sealed class DisabledPreferences : IUserNotificationPreferenceRepository
    {
        public Task<bool> IsEnabledAsync(
            Guid tenantId,
            Guid userId,
            NotificationCategory category,
            NotificationChannel channel,
            CancellationToken ct = default
        ) => Task.FromResult(false);

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
}
