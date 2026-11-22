using BuildingBlocks.Messaging.CommunicationIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Application.Consumers.Communication;
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
    public async Task Mobile_call_push_is_generic_owner_scoped_and_deduplicated()
    {
        var sender = new RecordingPushSender();
        var devices = new FakePushDeviceTokenRepository();
        devices.AddActiveDevice(TenantId, UserId, "callee-token");
        devices.AddActiveDevice(Guid.NewGuid(), UserId, "other-office-token");
        var dispatcher = BuildDispatcher(
            sender,
            devices,
            new RecordingNotificationLogRepository(),
            new NoOpUnitOfWork()
        );
        var evt = new CallStartedIntegrationEvent
        {
            TenantId = TenantId,
            CallId = Guid.NewGuid(),
            CallerUserId = Guid.NewGuid(),
            CalleeUserId = UserId,
            Kind = "Video",
            RingingAtUtc = DateTime.UtcNow,
        };
        await MobileCommunicationPushConsumer.Handle(
            evt,
            dispatcher,
            NullLogger<CallStartedIntegrationEvent>.Instance,
            TimeProvider.System,
            default
        );
        await MobileCommunicationPushConsumer.Handle(
            evt,
            dispatcher,
            NullLogger<CallStartedIntegrationEvent>.Instance,
            TimeProvider.System,
            default
        );
        var sent = Assert.Single(sender.Sent);
        Assert.Equal("callee-token", sent.Token);
        Assert.Equal(evt.CallId.ToString(), sent.Data!["callId"]);
        Assert.Equal(TenantId.ToString(), sent.Data["tenantId"]);
        Assert.Equal(UserId.ToString(), sent.Data["recipientUserId"]);
        Assert.DoesNotContain("callerUserId", sent.Data.Keys);
    }

    [Fact]
    public async Task Mobile_chat_push_skips_sender_duplicates_and_system_messages()
    {
        var sender = new RecordingPushSender();
        var devices = new FakePushDeviceTokenRepository();
        var author = Guid.NewGuid();
        devices.AddActiveDevice(TenantId, UserId, "recipient-token");
        devices.AddActiveDevice(TenantId, author, "sender-token");
        var dispatcher = BuildDispatcher(
            sender,
            devices,
            new RecordingNotificationLogRepository(),
            new NoOpUnitOfWork()
        );
        var evt = new ChatMessageSentIntegrationEvent
        {
            TenantId = TenantId,
            ConversationId = Guid.NewGuid(),
            MessageId = Guid.NewGuid(),
            SenderId = author,
            Kind = "Text",
            RecipientUserIds = [UserId, UserId, author],
            SentAtUtc = DateTime.UtcNow,
        };
        await ChatPushLane.RunAsync(evt, dispatcher);
        Assert.Equal("recipient-token", Assert.Single(sender.Sent).Token);
        Assert.Equal("You have a new message.", sender.Sent[0].Body);
        await ChatPushLane.RunAsync(evt with { Kind = "System", EventId = Guid.NewGuid() }, dispatcher);
        Assert.Single(sender.Sent);
    }

    [Fact]
    public async Task Mobile_call_push_does_not_ring_after_expiry()
    {
        var evt = new CallStartedIntegrationEvent
        {
            TenantId = TenantId,
            CallId = Guid.NewGuid(),
            CallerUserId = Guid.NewGuid(),
            CalleeUserId = UserId,
            Kind = "Audio",
            RingingAtUtc = DateTime.UtcNow.AddMinutes(-2),
        };
        await MobileCommunicationPushConsumer.Handle(
            evt,
            null!,
            NullLogger<CallStartedIntegrationEvent>.Instance,
            TimeProvider.System,
            default
        );
    }

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
                NotificationRecipient.ForPushTarget(UserId),
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

    [Fact]
    public async Task No_device_writes_no_log_row()
    {
        var sender = new RecordingPushSender();
        var logs = new RecordingNotificationLogRepository();
        var metrics = new CountingDispatchMetrics();
        var dispatcher = BuildDispatcher(sender, new FakePushDeviceTokenRepository(), logs, metrics);

        var result = await dispatcher.SendPushAsync(FormPush(Guid.NewGuid()));

        Assert.Equal("Notification.NoPushDevices", result.Error.Code);
        Assert.Empty(sender.Sent);
        Assert.Empty(logs.Logs);
        Assert.Equal(0, metrics.Failed);
        Assert.Equal(1, metrics.NoRecipient);
    }

    [Fact]
    public async Task Platform_filter_limits_the_devices()
    {
        var sender = new RecordingPushSender();
        var devices = new FakePushDeviceTokenRepository();
        devices.AddActiveDevice(TenantId, UserId, "android-token");
        devices.AddActiveDevice(TenantId, UserId, "web-token", PushPlatform.Web);
        var dispatcher = BuildDispatcher(
            sender,
            devices,
            new RecordingNotificationLogRepository(),
            new NoOpUnitOfWork()
        );

        await dispatcher.SendPushAsync(
            FormPush(Guid.NewGuid()) with
            {
                Platforms = new HashSet<PushPlatform> { PushPlatform.Fcm },
                TimeToLive = TimeSpan.FromSeconds(45),
            }
        );

        var message = Assert.Single(sender.Sent);
        Assert.Equal("android-token", message.Token);
        Assert.Equal(TimeSpan.FromSeconds(45), message.TimeToLive);
    }

    [Fact]
    public async Task Con_dispositivo_y_error_de_FCM_el_log_queda_Failed()
    {
        var sender = new RecordingPushSender();
        sender.FailNextWith(new Error(PushErrorCodes.Unavailable, "fcm down"));
        var devices = new FakePushDeviceTokenRepository();
        devices.AddActiveDevice(TenantId, UserId, "token");
        var logs = new RecordingNotificationLogRepository();
        var metrics = new CountingDispatchMetrics();
        var dispatcher = BuildDispatcher(sender, devices, logs, metrics);

        var outcome = await dispatcher.DispatchPushAsync(FormPush(Guid.NewGuid()));

        Assert.Equal(PushErrorCodes.Unavailable, outcome.Error?.Code);
        Assert.Single(sender.Sent);
        var log = Assert.Single(logs.Logs);
        Assert.Equal(NotificationStatus.Failed, log.Status);
        Assert.Equal("fcm down", log.Error);
        Assert.Equal(1, metrics.Failed);
    }

    [Fact]
    public async Task Un_proveedor_sin_configurar_deja_el_log_Failed_y_no_cuenta_como_enviado()
    {
        var sender = new RecordingPushSender();
        sender.FailNextWith(new Error(PushErrorCodes.NotConfigured, "Push provider is not configured."));
        var devices = new FakePushDeviceTokenRepository();
        devices.AddActiveDevice(TenantId, UserId, "token");
        var logs = new RecordingNotificationLogRepository();
        var metrics = new CountingDispatchMetrics();
        var dispatcher = BuildDispatcher(sender, devices, logs, metrics);

        var outcome = await dispatcher.DispatchPushAsync(FormPush(Guid.NewGuid()));

        Assert.Equal(PushErrorCodes.NotConfigured, outcome.Error?.Code);
        var log = Assert.Single(logs.Logs);
        Assert.Equal(NotificationStatus.Failed, log.Status);
        Assert.Equal("Push provider is not configured.", log.Error);
        Assert.Equal(1, metrics.Failed);
        Assert.Equal(0, metrics.Sent);
        Assert.Empty(devices.RevokedIds);
        Assert.Empty(outcome.RetryableDeviceIds);
    }

    [Fact]
    public async Task Con_dispositivo_y_envio_exitoso_el_log_queda_Sent()
    {
        var sender = new RecordingPushSender();
        var devices = new FakePushDeviceTokenRepository();
        devices.AddActiveDevice(TenantId, UserId, "token");
        var logs = new RecordingNotificationLogRepository();
        var metrics = new CountingDispatchMetrics();
        var dispatcher = BuildDispatcher(sender, devices, logs, metrics);

        var outcome = await dispatcher.DispatchPushAsync(FormPush(Guid.NewGuid()));

        Assert.Null(outcome.Error);
        Assert.Single(sender.Sent);
        var log = Assert.Single(logs.Logs);
        Assert.Equal(NotificationStatus.Sent, log.Status);
        Assert.Null(log.Error);
        Assert.Equal(1, metrics.Sent);
        Assert.Equal(0, metrics.Failed);
    }

    // N8-04: encolar no llama a FCM ni escribe log; el envío lo hace el carril de push.
    [Fact]
    public async Task Enqueue_does_not_call_the_push_sender()
    {
        var sender = new RecordingPushSender();
        var devices = new FakePushDeviceTokenRepository();
        devices.AddActiveDevice(TenantId, UserId, "token");
        var logs = new RecordingNotificationLogRepository();
        var queue = new RecordingPushDeliveryQueue();
        var dispatcher = BuildDispatcher(sender, devices, logs, queue);
        var request = FormPush(Guid.NewGuid());

        var result = await dispatcher.EnqueuePushAsync(request);

        Assert.True(result.IsSuccess);
        Assert.Empty(sender.Sent);
        Assert.Empty(logs.Logs);
        var queued = Assert.Single(queue.Queued);
        Assert.Equal(TenantId, queued.TenantId);
        Assert.Same(request, queued.Request);
    }

    [Fact]
    public async Task Enqueue_without_devices_queues_nothing()
    {
        var queue = new RecordingPushDeliveryQueue();
        var dispatcher = BuildDispatcher(
            new RecordingPushSender(),
            new FakePushDeviceTokenRepository(),
            new RecordingNotificationLogRepository(),
            queue
        );

        var result = await dispatcher.EnqueuePushAsync(FormPush(Guid.NewGuid()));

        Assert.Equal("Notification.NoPushDevices", result.Error.Code);
        Assert.Empty(queue.Queued);
    }

    // La intención encola el push; la llamada entrante sigue enviando en el handler.
    [Fact]
    public async Task Intent_push_is_queued_and_the_incoming_call_is_not()
    {
        var sender = new RecordingPushSender();
        var devices = new FakePushDeviceTokenRepository();
        devices.AddActiveDevice(TenantId, UserId, "token");
        var queue = new RecordingPushDeliveryQueue();
        var dispatcher = BuildDispatcher(sender, devices, new RecordingNotificationLogRepository(), queue);

        var summary = await dispatcher.DispatchAsync(
            new NotificationIntent(
                TaxVision.Notification.Domain.Catalog.NotificationKinds.CommunicationChatMessage,
                TenantId,
                new NotificationTarget(UserId),
                Guid.NewGuid(),
                "communication.chat.message_sent.v1",
                DateTime.UtcNow,
                "corr",
                Push: new PushContent(Guid.NewGuid().ToString(), LinkForRecipient: true)
            )
        );
        await MobileCommunicationPushConsumer.Handle(
            new CallStartedIntegrationEvent
            {
                TenantId = TenantId,
                CallId = Guid.NewGuid(),
                CallerUserId = Guid.NewGuid(),
                CalleeUserId = UserId,
                Kind = "Audio",
                RingingAtUtc = DateTime.UtcNow,
            },
            dispatcher,
            NullLogger<CallStartedIntegrationEvent>.Instance,
            TimeProvider.System,
            default
        );

        Assert.Equal(ChannelOutcome.Queued, summary.Channels[NotificationChannel.Push]);
        Assert.Single(queue.Queued);
        Assert.Single(sender.Sent);
    }

    private static NotificationDispatcher BuildDispatcher(
        IPushSender pushSender,
        IPushDeviceTokenRepository pushDeviceTokens,
        INotificationLogRepository logs,
        IPushDeliveryQueue pushQueue
    ) =>
        TestDispatchers.Create(
            NoOpSmsSender.Instance,
            pushSender,
            pushDeviceTokens,
            logs,
            TestDeliveryGates.For(
                AlwaysEnabledUserNotificationPreferenceRepository.Instance,
                new TaxVision.Notification.Tests.TestDoubles.InMemoryUserEmailDirectoryRepository()
            ),
            new NoOpIntegrationEventPublisher(),
            new NoOpUnitOfWork(),
            new NoOpNotificationDispatchMetrics(),
            new TaxVision.Notification.Tests.TestDoubles.InlinePushAttemptRecorder(logs),
            pushQueue: pushQueue
        );

    private static NotificationDispatcher BuildDispatcher(
        IPushSender pushSender,
        IPushDeviceTokenRepository pushDeviceTokens,
        INotificationLogRepository logs,
        INotificationDispatchMetrics metrics
    ) =>
        TestDispatchers.Create(
            NoOpSmsSender.Instance,
            pushSender,
            pushDeviceTokens,
            logs,
            TestDeliveryGates.For(
                AlwaysEnabledUserNotificationPreferenceRepository.Instance,
                new TaxVision.Notification.Tests.TestDoubles.InMemoryUserEmailDirectoryRepository()
            ),
            new NoOpIntegrationEventPublisher(),
            new NoOpUnitOfWork(),
            metrics,
            new TaxVision.Notification.Tests.TestDoubles.InlinePushAttemptRecorder(logs)
        );

    private sealed class CountingDispatchMetrics : INotificationDispatchMetrics
    {
        public void RecordClientFormPushFailure(string code) { }

        public void RecordNotAllowedByCatalog(string kind, string channel) { }

        public void RecordDelivered(string kind, string channel) { }

        public int Sent { get; private set; }
        public int Failed { get; private set; }
        public int NoRecipient { get; private set; }

        public void RecordSent(string kind, string channel) => Sent++;

        public void RecordSuppressedByPreference(string kind, string channel) { }

        public void RecordFailed(string kind, string channel) => Failed++;

        public void RecordNoRecipient(string kind, string channel) => NoRecipient++;
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
        TestDispatchers.Create(
            NoOpSmsSender.Instance,
            pushSender,
            pushDeviceTokens,
            logs,
            TestDeliveryGates.For(
                AlwaysEnabledUserNotificationPreferenceRepository.Instance,
                new TaxVision.Notification.Tests.TestDoubles.InMemoryUserEmailDirectoryRepository()
            ),
            new NoOpIntegrationEventPublisher(),
            uow,
            new NoOpNotificationDispatchMetrics(),
            new TaxVision.Notification.Tests.TestDoubles.InlinePushAttemptRecorder(logs)
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

        public Task<int> RevokeAllActiveForUserAsync(Guid tenantId, Guid userId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        private readonly Dictionary<Guid, PushDeviceToken> _devices = [];
        public List<Guid> RevokedIds { get; } = [];

        public Guid AddActiveDevice(Guid tenantId, Guid userId, string token, PushPlatform platform = PushPlatform.Fcm)
        {
            var device = PushDeviceToken.Register(tenantId, userId, platform, token, deviceId: null).Value;
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
            string? idempotencyToken,
            string? sourceRef = null
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
