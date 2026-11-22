using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Application.Push.Consumers;
using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Domain.Preferences;
using TaxVision.Notification.Tests.TestDoubles;

namespace TaxVision.Notification.Tests;

/// <summary>N8-04: el carril de push envía a FCM, registra el resultado y deduplica por evento y destinatario.</summary>
public sealed class DeliverPushConsumerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    [Fact]
    public async Task Delivers_and_records_the_result()
    {
        var lane = new Lane();

        await lane.DeliverAsync(Guid.NewGuid());

        Assert.Equal(["token"], lane.Sender.Attempted);
        Assert.Equal(NotificationStatus.Sent, Assert.Single(lane.Logs.All).Status);
    }

    [Fact]
    public async Task Second_delivery_of_the_same_event_is_deduplicated()
    {
        var lane = new Lane();
        var eventId = Guid.NewGuid();

        await lane.DeliverAsync(eventId);
        await lane.DeliverAsync(eventId);

        Assert.Single(lane.Sender.Attempted);
        Assert.Single(lane.Logs.All);
    }

    // R-20: el intento fallido queda Failed y no frena el reintento del comando.
    [Fact]
    public async Task Retry_after_a_transient_failure_sends_again()
    {
        var lane = new Lane();
        var eventId = Guid.NewGuid();
        lane.Sender.FailNext(PushErrorCodes.Unavailable);

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => lane.DeliverAsync(eventId));
        await lane.DeliverAsync(eventId);

        Assert.Equal(PushErrorCodes.Unavailable, error.Message);
        Assert.Equal(["token", "token"], lane.Sender.Attempted);
        Assert.Equal([NotificationStatus.Failed, NotificationStatus.Sent], lane.Logs.All.Select(log => log.Status));
    }

    [Fact]
    public async Task A_recent_pending_attempt_is_not_resent()
    {
        var lane = new Lane();
        var eventId = Guid.NewGuid();
        lane.Logs.All.Add(PendingLog(eventId));

        await lane.DeliverAsync(eventId);

        Assert.Empty(lane.Sender.Attempted);
    }

    // Un Pending viejo es un intento que murió sin resultado: ya no hay envío en curso que proteger.
    [Fact]
    public async Task A_stale_pending_attempt_is_resent()
    {
        var lane = new Lane(DateTimeOffset.UtcNow.AddMinutes(10));
        var eventId = Guid.NewGuid();
        lane.Logs.All.Add(PendingLog(eventId));

        await lane.DeliverAsync(eventId);

        Assert.Equal(["token"], lane.Sender.Attempted);
    }

    [Fact]
    public async Task A_permanent_failure_is_logged_and_not_retried()
    {
        var lane = new Lane();
        lane.Sender.FailNext(PushErrorCodes.NotConfigured);

        await lane.DeliverAsync(Guid.NewGuid());

        Assert.Equal(NotificationStatus.Failed, Assert.Single(lane.Logs.All).Status);
        Assert.Single(lane.Logger.Warnings);
    }

    private static NotificationLog PendingLog(Guid eventId) =>
        NotificationLog
            .Create(
                TenantId,
                NotificationChannel.Push,
                NotificationRecipient.ForPushTarget(UserId),
                "Title",
                "task.overdue.push",
                eventId,
                "corr",
                "tasks.overdue"
            )
            .Value;

    private sealed class Lane
    {
        public Lane(DateTimeOffset? now = null)
        {
            var devices = new DeviceList();
            devices.Add(UserId, "token");
            Dispatcher = new NotificationDispatcherHarness(
                Sender,
                devices,
                Logs,
                clock: now is { } at ? new FixedClock(at) : null
            ).Dispatcher;
        }

        public ScriptedSender Sender { get; } = new();
        public LatestFirstLogs Logs { get; } = new();
        public WarningLogger Logger { get; } = new();
        public NotificationDispatcher Dispatcher { get; }

        public Task DeliverAsync(Guid eventId) =>
            DeliverPushConsumer.Handle(
                new DeliverPush(
                    TenantId,
                    new PushDispatchRequest(
                        TenantId,
                        UserId,
                        "Title",
                        "Body",
                        NotificationCategory.Collaboration,
                        "tasks.overdue",
                        "task.overdue.push",
                        eventId,
                        "corr"
                    )
                ),
                Dispatcher,
                Logger,
                default
            );
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class ScriptedSender : IPushSender
    {
        private readonly Queue<string> _failures = new();
        public List<string> Attempted { get; } = [];

        public void FailNext(string code) => _failures.Enqueue(code);

        public Task<Result> SendAsync(PushMessage message, CancellationToken ct = default)
        {
            Attempted.Add(message.Token);
            return Task.FromResult(
                _failures.TryDequeue(out var code)
                    ? Result.Failure(new Error(code, "scripted failure"))
                    : Result.Success()
            );
        }
    }

    // Como el repositorio real: el log más reciente del evento manda.
    private sealed class LatestFirstLogs : INotificationLogRepository
    {
        public List<NotificationLog> All { get; } = [];

        public Task AddAsync(NotificationLog log, CancellationToken ct = default)
        {
            All.Add(log);
            return Task.CompletedTask;
        }

        public Task<NotificationLog?> GetByRelatedEventAsync(
            Guid tenantId,
            Guid relatedEventId,
            string kind,
            NotificationChannel channel,
            string recipient,
            CancellationToken ct = default
        ) =>
            Task.FromResult(
                All.LastOrDefault(log =>
                    log.TenantId == tenantId
                    && log.RelatedEventId == relatedEventId
                    && log.Kind == kind
                    && log.Channel == channel
                    && log.Recipient == recipient
                )
            );

        public Task<(IReadOnlyList<NotificationLog> Items, int TotalCount)> GetPagedAsync(
            Guid tenantId,
            NotificationStatus? status,
            int page,
            int size,
            CancellationToken ct = default
        ) => Task.FromResult<(IReadOnlyList<NotificationLog>, int)>((All, All.Count));

        public Task<NotificationLog?> GetByRelatedEventIdAsync(
            Guid tenantId,
            Guid relatedEventId,
            string templateKey,
            string recipient,
            CancellationToken ct = default
        ) => Task.FromResult<NotificationLog?>(null);
    }

    private sealed class DeviceList : IPushDeviceTokenRepository
    {
        private readonly List<PushDeviceToken> _devices = [];

        public void Add(Guid userId, string token) =>
            _devices.Add(PushDeviceToken.Register(TenantId, userId, PushPlatform.Fcm, token, deviceId: null).Value);

        public Task<IReadOnlyList<PushDeviceToken>> ListActiveForUserAsync(
            Guid tenantId,
            Guid userId,
            CancellationToken ct = default
        ) =>
            Task.FromResult<IReadOnlyList<PushDeviceToken>>(
                _devices.Where(d => d.TenantId == tenantId && d.UserId == userId && d.IsActive).ToList()
            );

        public Task RevokeAsync(Guid tenantId, Guid id, CancellationToken ct = default) => Task.CompletedTask;

        public Task<int> RevokeForSessionAsync(
            Guid tenantId,
            Guid userId,
            Guid sessionId,
            CancellationToken ct = default
        ) => Task.FromResult(0);

        public Task<int> RevokeAllActiveForUserExceptSessionAsync(
            Guid tenantId,
            Guid userId,
            Guid? exceptSessionId,
            CancellationToken ct = default
        ) => Task.FromResult(0);

        public Task<int> DeactivateTokenInOtherTenantsAsync(
            string token,
            Guid exceptTenantId,
            CancellationToken ct = default
        ) => Task.FromResult(0);

        public Task AddAsync(PushDeviceToken token, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<PushDeviceToken?> FindByTokenAsync(Guid tenantId, string token, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<PushDeviceToken?> GetAsync(Guid tenantId, Guid id, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<int> RevokeAllActiveForUserAsync(Guid tenantId, Guid userId, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class WarningLogger : ILogger<DeliverPush>
    {
        public List<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            if (logLevel == LogLevel.Warning)
                Warnings.Add(formatter(state, exception));
        }
    }
}
