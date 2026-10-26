using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Domain.Preferences;
using TaxVision.Notification.Tests.TestDoubles;

namespace TaxVision.Notification.Tests;

/// <summary>
/// Fase 7 del plan de notificaciones dinámicas — cobertura nueva de
/// <see cref="NotificationDispatcher.SendPushAsync"/>, sin cobertura previa (confirmado antes
/// de escribir esta suite). Cubre el fan-out best-effort a múltiples dispositivos y la
/// revocación automática de un <see cref="PushDeviceToken"/> cuando el proveedor confirma
/// <see cref="PushErrorCodes.TokenInvalid"/>.
/// </summary>
public sealed class NotificationDispatcherPushTests
{
    [Fact]
    public async Task SendPushAsync_entrega_a_un_solo_dispositivo_activo_y_marca_Sent()
    {
        var pushSender = new RecordingPushSender();
        var pushDeviceTokens = new FakePushDeviceTokenRepository();
        var deviceId = pushDeviceTokens.AddActiveDevice(tenantId: TenantId, userId: UserId, token: "token-1");
        var logs = new RecordingNotificationLogRepository();
        var uow = new NoOpUnitOfWork();
        var dispatcher = BuildDispatcher(pushSender, pushDeviceTokens, logs, uow);

        var result = await dispatcher.SendPushAsync(
            TenantId,
            UserId,
            "Titulo",
            "Cuerpo",
            NotificationCategory.DocumentsAndSignatures,
            "sig.test.v1",
            relatedEventId: null,
            correlationId: null
        );

        Assert.True(result.IsSuccess);
        Assert.Single(pushSender.Sent);
        Assert.Equal("token-1", pushSender.Sent[0].Token);
        Assert.Single(logs.Logs);
        Assert.Equal(NotificationStatus.Sent, logs.Logs[0].Status);
        Assert.DoesNotContain(deviceId, pushDeviceTokens.RevokedIds);
    }

    [Fact]
    public async Task SendPushAsync_fanout_best_effort_un_dispositivo_entregado_ya_cuenta_como_Sent()
    {
        var pushSender = new RecordingPushSender();
        pushSender.FailNextWith(new Error("Notification.PushFailed", "device offline"));
        var pushDeviceTokens = new FakePushDeviceTokenRepository();
        pushDeviceTokens.AddActiveDevice(TenantId, UserId, "token-fails-first");
        pushDeviceTokens.AddActiveDevice(TenantId, UserId, "token-succeeds-second");
        var logs = new RecordingNotificationLogRepository();
        var uow = new NoOpUnitOfWork();
        var dispatcher = BuildDispatcher(pushSender, pushDeviceTokens, logs, uow);

        var result = await dispatcher.SendPushAsync(
            TenantId,
            UserId,
            "Titulo",
            "Cuerpo",
            NotificationCategory.DocumentsAndSignatures,
            "sig.test.v1",
            relatedEventId: null,
            correlationId: null
        );

        Assert.True(result.IsSuccess);
        Assert.Equal(2, pushSender.Sent.Count);
        Assert.Equal(NotificationStatus.Sent, logs.Logs[0].Status);
    }

    [Fact]
    public async Task SendPushAsync_revoca_el_device_cuando_el_proveedor_confirma_TokenInvalid()
    {
        var pushSender = new RecordingPushSender();
        pushSender.FailNextWith(new Error(PushErrorCodes.TokenInvalid, "FCM token is no longer registered."));
        var pushDeviceTokens = new FakePushDeviceTokenRepository();
        var deadDeviceId = pushDeviceTokens.AddActiveDevice(TenantId, UserId, "token-uninstalled");
        var logs = new RecordingNotificationLogRepository();
        var uow = new NoOpUnitOfWork();
        var dispatcher = BuildDispatcher(pushSender, pushDeviceTokens, logs, uow);

        var result = await dispatcher.SendPushAsync(
            TenantId,
            UserId,
            "Titulo",
            "Cuerpo",
            NotificationCategory.DocumentsAndSignatures,
            "sig.test.v1",
            relatedEventId: null,
            correlationId: null
        );

        Assert.False(result.IsSuccess);
        Assert.Contains(deadDeviceId, pushDeviceTokens.RevokedIds);
    }

    [Fact]
    public async Task SendPushAsync_sin_dispositivos_activos_falla_con_NoPushDevices_y_no_intenta_revocar()
    {
        var pushSender = new RecordingPushSender();
        var pushDeviceTokens = new FakePushDeviceTokenRepository();
        var logs = new RecordingNotificationLogRepository();
        var uow = new NoOpUnitOfWork();
        var dispatcher = BuildDispatcher(pushSender, pushDeviceTokens, logs, uow);

        var result = await dispatcher.SendPushAsync(
            TenantId,
            UserId,
            "Titulo",
            "Cuerpo",
            NotificationCategory.DocumentsAndSignatures,
            "sig.test.v1",
            relatedEventId: null,
            correlationId: null
        );

        Assert.False(result.IsSuccess);
        Assert.Equal("Notification.NoPushDevices", result.Error.Code);
        Assert.Empty(pushSender.Sent);
        Assert.Empty(pushDeviceTokens.RevokedIds);
    }

    [Theory]
    [InlineData(PushErrorCodes.InvalidArgument)]
    [InlineData(PushErrorCodes.SenderIdMismatch)]
    [InlineData(PushErrorCodes.ThirdPartyAuthError)]
    [InlineData(PushErrorCodes.Unavailable)]
    public async Task SendPushAsync_no_revoca_el_device_para_errores_que_no_son_TokenInvalid(string errorCode)
    {
        var pushSender = new RecordingPushSender();
        pushSender.FailNextWith(new Error(errorCode, "provider failure"));
        var devices = new FakePushDeviceTokenRepository();
        devices.AddActiveDevice(TenantId, UserId, "active-token");
        var dispatcher = BuildDispatcher(
            pushSender,
            devices,
            new RecordingNotificationLogRepository(),
            new NoOpUnitOfWork()
        );

        var result = await dispatcher.SendPushAsync(
            TenantId,
            UserId,
            "Title",
            "Body",
            NotificationCategory.ClientRequests,
            "tasks.form_request_assigned",
            Guid.NewGuid(),
            "corr"
        );

        Assert.True(result.IsFailure);
        Assert.Empty(devices.RevokedIds);
    }

    [Fact]
    public async Task SendPushAsync_deduplica_un_evento_ya_enviado_por_kind_y_destinatario()
    {
        var eventId = Guid.NewGuid();
        var sender = new RecordingPushSender();
        var devices = new FakePushDeviceTokenRepository();
        devices.AddActiveDevice(TenantId, UserId, "active-token");
        var logs = new RecordingNotificationLogRepository();
        var existing = NotificationLog
            .Create(
                TenantId,
                NotificationChannel.Push,
                UserId.ToString(),
                "Title",
                "task.form_request_assigned.push.v1",
                eventId,
                "corr",
                "tasks.form_request_assigned"
            )
            .Value;
        existing.MarkSent();
        logs.Logs.Add(existing);
        var dispatcher = BuildDispatcher(sender, devices, logs, new NoOpUnitOfWork());

        var result = await dispatcher.SendPushAsync(
            new PushDispatchRequest(
                TenantId,
                UserId,
                "Title",
                "Body",
                NotificationCategory.ClientRequests,
                "tasks.form_request_assigned",
                "task.form_request_assigned.push.v1",
                eventId,
                "corr"
            )
        );

        Assert.True(result.IsSuccess);
        Assert.Empty(sender.Sent);
        Assert.Single(logs.Logs);
    }

    [Fact]
    public async Task SendPushAsync_separa_kind_de_template_y_propaga_el_payload_web()
    {
        var sender = new RecordingPushSender();
        var devices = new FakePushDeviceTokenRepository();
        devices.AddActiveDevice(TenantId, UserId, "web-token");
        var logs = new RecordingNotificationLogRepository();
        var dispatcher = BuildDispatcher(sender, devices, logs, new NoOpUnitOfWork());
        var data = new Dictionary<string, string> { ["kind"] = "tasks.form_request_assigned" };

        var result = await dispatcher.SendPushAsync(
            new PushDispatchRequest(
                TenantId,
                UserId,
                "Title",
                "Body",
                NotificationCategory.ClientRequests,
                "tasks.form_request_assigned",
                "task.form_request_assigned.push.v1",
                Guid.NewGuid(),
                "corr",
                data,
                "topic",
                "tag",
                "https://office.taxproffice.com/n/open?rt=form_request&rid=1"
            )
        );

        Assert.True(result.IsSuccess);
        var message = Assert.Single(sender.Sent);
        Assert.Same(data, message.Data);
        Assert.Equal("topic", message.Topic);
        Assert.Equal("tag", message.Tag);
        Assert.Equal("https://office.taxproffice.com/n/open?rt=form_request&rid=1", message.Link);
        var log = Assert.Single(logs.Logs);
        Assert.Equal("tasks.form_request_assigned", log.Kind);
        Assert.Equal("task.form_request_assigned.push.v1", log.TemplateKey);
    }

    [Fact]
    public async Task DispatchPushAsync_reports_only_the_transiently_failed_device_and_revokes_only_the_invalid_one()
    {
        var sender = new TokenScriptedPushSender(
            new() { ["bad-network"] = PushErrorCodes.Unavailable, ["dead"] = PushErrorCodes.TokenInvalid }
        );
        var devices = new FakePushDeviceTokenRepository();
        devices.AddActiveDevice(TenantId, UserId, "ok");
        var flaky = devices.AddActiveDevice(TenantId, UserId, "bad-network");
        var dead = devices.AddActiveDevice(TenantId, UserId, "dead");
        var logs = new RecordingNotificationLogRepository();
        var dispatcher = BuildDispatcher(sender, devices, logs, new NoOpUnitOfWork());

        var outcome = await dispatcher.DispatchPushAsync(FormPush(Guid.NewGuid()));

        Assert.Null(outcome.Error);
        Assert.Equal([flaky], outcome.RetryableDeviceIds);
        Assert.Equal([dead], devices.RevokedIds);
        Assert.Equal(NotificationStatus.Sent, Assert.Single(logs.Logs).Status);
    }

    [Fact]
    public async Task A_device_retry_reaches_only_that_device_even_when_the_event_was_already_sent()
    {
        var eventId = Guid.NewGuid();
        var sender = new TokenScriptedPushSender(new() { ["bad-network"] = PushErrorCodes.Unavailable });
        var devices = new FakePushDeviceTokenRepository();
        devices.AddActiveDevice(TenantId, UserId, "ok");
        var flaky = devices.AddActiveDevice(TenantId, UserId, "bad-network");
        var logs = new RecordingNotificationLogRepository();
        var dispatcher = BuildDispatcher(sender, devices, logs, new NoOpUnitOfWork());
        await dispatcher.DispatchPushAsync(FormPush(eventId));
        sender.Fail.Clear();

        var retry = await dispatcher.DispatchPushAsync(FormPush(eventId) with { DeviceIds = [flaky] });
        var duplicate = await dispatcher.DispatchPushAsync(FormPush(eventId) with { DeviceIds = [flaky] });

        Assert.Null(retry.Error);
        Assert.Empty(retry.RetryableDeviceIds);
        Assert.Null(duplicate.Error);
        Assert.Equal(["ok", "bad-network", "bad-network"], sender.Sent.Select(message => message.Token));
    }

    [Fact]
    public async Task A_provider_exception_counts_as_unavailable_and_never_escapes()
    {
        var sender = new ThrowingPushSender();
        var devices = new FakePushDeviceTokenRepository();
        var deviceId = devices.AddActiveDevice(TenantId, UserId, "token");
        var dispatcher = BuildDispatcher(
            sender,
            devices,
            new RecordingNotificationLogRepository(),
            new NoOpUnitOfWork()
        );

        var outcome = await dispatcher.DispatchPushAsync(FormPush(Guid.NewGuid()));

        Assert.Equal(PushErrorCodes.Unavailable, outcome.Error?.Code);
        Assert.Equal([deviceId], outcome.RetryableDeviceIds);
        Assert.Empty(devices.RevokedIds);
    }

    private static PushDispatchRequest FormPush(Guid eventId) =>
        new(
            TenantId,
            UserId,
            "Title",
            "Body",
            NotificationCategory.ClientRequests,
            "tasks.form_request_assigned",
            "task.form_request_assigned.push.v1",
            eventId,
            "corr"
        );

    private sealed class TokenScriptedPushSender(Dictionary<string, string> failures) : IPushSender
    {
        public Dictionary<string, string> Fail { get; } = failures;
        public List<PushMessage> Sent { get; } = [];

        public Task<Result> SendAsync(PushMessage message, CancellationToken ct = default)
        {
            Sent.Add(message);
            return Task.FromResult(
                Fail.TryGetValue(message.Token, out var code)
                    ? Result.Failure(new Error(code, "scripted failure"))
                    : Result.Success()
            );
        }
    }

    private sealed class ThrowingPushSender : IPushSender
    {
        public Task<Result> SendAsync(PushMessage message, CancellationToken ct = default) =>
            throw new HttpRequestException("network down");
    }

    private static readonly Guid TenantId = Guid.NewGuid();
    private static readonly Guid UserId = Guid.NewGuid();

    private static NotificationDispatcher BuildDispatcher(
        IPushSender pushSender,
        IPushDeviceTokenRepository pushDeviceTokens,
        INotificationLogRepository logs,
        IUnitOfWork uow
    ) =>
        new(
            NoOpSmsSender.Instance,
            pushSender,
            pushDeviceTokens,
            logs,
            AlwaysEnabledUserNotificationPreferenceRepository.Instance,
            new NoOpIntegrationEventPublisher(),
            uow,
            new NoOpNotificationDispatchMetrics(),
            NullLogger<NotificationDispatcher>.Instance
        );

    private sealed class NoOpIntegrationEventPublisher : IIntegrationEventPublisher
    {
        public Task PublishAsync<T>(T message, CancellationToken ct = default)
            where T : class => Task.CompletedTask;

        public Task SendToQueueAsync<T>(string queueName, T message, CancellationToken ct = default)
            where T : class => Task.CompletedTask;
    }

    // ------------------------------------------------------------------
    // Fakes — mismo estilo hand-rolled que BaselineSmokeTests, sin librería de mocking.
    // ------------------------------------------------------------------

    private sealed class RecordingPushSender : IPushSender
    {
        public List<PushMessage> Sent { get; } = [];
        private Error? _nextFailure;

        public void FailNextWith(Error error) => _nextFailure = error;

        public Task<Result> SendAsync(PushMessage message, CancellationToken ct = default)
        {
            Sent.Add(message);
            if (_nextFailure is { } error)
            {
                _nextFailure = null;
                return Task.FromResult(Result.Failure(error));
            }
            return Task.FromResult(Result.Success());
        }
    }

    private sealed class FakePushDeviceTokenRepository : IPushDeviceTokenRepository
    {
        private readonly Dictionary<Guid, PushDeviceToken> _devices = [];
        public List<Guid> RevokedIds { get; } = [];

        public Guid AddActiveDevice(Guid tenantId, Guid userId, string token)
        {
            var device = PushDeviceToken.Register(tenantId, userId, PushPlatform.Fcm, token, deviceId: null).Value;
            _devices[device.Id] = device;
            return device.Id;
        }

        public Task AddAsync(PushDeviceToken token, CancellationToken ct = default)
        {
            _devices[token.Id] = token;
            return Task.CompletedTask;
        }

        public Task<PushDeviceToken?> FindByTokenAsync(Guid tenantId, string token, CancellationToken ct = default) =>
            Task.FromResult(_devices.Values.FirstOrDefault(d => d.TenantId == tenantId && d.Token == token));

        public Task<PushDeviceToken?> GetAsync(Guid tenantId, Guid id, CancellationToken ct = default) =>
            Task.FromResult(_devices.TryGetValue(id, out var d) && d.TenantId == tenantId ? d : null);

        public Task<IReadOnlyList<PushDeviceToken>> ListActiveForUserAsync(
            Guid tenantId,
            Guid userId,
            CancellationToken ct = default
        ) =>
            Task.FromResult<IReadOnlyList<PushDeviceToken>>(
                _devices.Values.Where(d => d.TenantId == tenantId && d.UserId == userId && d.IsActive).ToList()
            );

        public Task RevokeAsync(Guid tenantId, Guid id, CancellationToken ct = default)
        {
            if (_devices.TryGetValue(id, out var d) && d.TenantId == tenantId)
            {
                d.Revoke();
                RevokedIds.Add(id);
            }
            return Task.CompletedTask;
        }
    }

    private sealed class RecordingNotificationLogRepository : INotificationLogRepository
    {
        public List<NotificationLog> Logs { get; } = [];

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

        public Task<NotificationLog?> GetByRelatedEventAsync(
            Guid tenantId,
            Guid relatedEventId,
            string kind,
            NotificationChannel channel,
            string recipient,
            CancellationToken ct = default
        ) =>
            Task.FromResult(
                Logs.FirstOrDefault(log =>
                    log.TenantId == tenantId
                    && log.RelatedEventId == relatedEventId
                    && log.Kind == kind
                    && log.Channel == channel
                    && log.Recipient == recipient
                )
            );
    }

    private sealed class NoOpUnitOfWork : IUnitOfWork
    {
        public int SaveCount { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken ct = default)
        {
            SaveCount++;
            return Task.FromResult(0);
        }
    }

    private sealed class NoOpSmsSender : ISmsSender
    {
        public static readonly NoOpSmsSender Instance = new();

        public Task<Result> SendAsync(
            string phoneNumber,
            string text,
            CancellationToken ct,
            string? templateKey,
            string? idempotencyToken
        ) => Task.FromResult(Result.Success());
    }

    private sealed class AlwaysEnabledUserNotificationPreferenceRepository : IUserNotificationPreferenceRepository
    {
        public static readonly AlwaysEnabledUserNotificationPreferenceRepository Instance = new();

        public Task<bool> IsEnabledAsync(
            Guid tenantId,
            Guid userId,
            NotificationCategory category,
            NotificationChannel channel,
            CancellationToken ct = default
        ) => Task.FromResult(true);

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
        ) => Task.FromResult<IReadOnlyList<UserNotificationPreference>>(Array.Empty<UserNotificationPreference>());

        public Task AddAsync(UserNotificationPreference preference, CancellationToken ct = default) =>
            Task.CompletedTask;
    }
}
