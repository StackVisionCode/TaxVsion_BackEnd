using BuildingBlocks.Messaging.CommunicationIntegrationEvents;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Consumers.Communication;
using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Tests.TestDoubles;

namespace TaxVision.Notification.Tests;

/// <summary>Push de chat y llamada: solo un fallo transitorio lanza; el resto no frena a los demás.</summary>
public sealed class MobileCommunicationPushConsumerTests
{
    private static readonly Guid TenantId = Guid.NewGuid();

    // N8-03: el handler del evento no envía; deja el trabajo en el carril masivo.
    [Fact]
    public void Chat_event_is_forwarded_to_the_bulk_lane()
    {
        var recipients = new[] { Guid.NewGuid(), Guid.NewGuid() };
        var evt = ChatTo(recipients) with { PushRecipientUserIds = [recipients[0]], CorrelationId = "corr" };

        var command = MobileCommunicationPushConsumer.Handle(evt);

        Assert.NotNull(command);
        Assert.Equal(evt.TenantId, command.TenantId);
        Assert.Equal(evt.EventId, command.EventId);
        Assert.Equal(evt.ConversationId, command.ConversationId);
        Assert.Equal(evt.MessageId, command.MessageId);
        Assert.Equal(evt.SenderId, command.SenderId);
        Assert.Equal(recipients, command.RecipientUserIds);
        Assert.Equal([recipients[0]], command.PushRecipientUserIds!);
        Assert.Equal(evt.SentAtUtc, command.SentAtUtc);
        Assert.Equal("corr", command.CorrelationId);
    }

    [Fact]
    public void A_system_or_meeting_message_is_not_forwarded()
    {
        var evt = ChatTo([Guid.NewGuid()]);

        Assert.Null(MobileCommunicationPushConsumer.Handle(evt with { Kind = "System" }));
        Assert.Null(MobileCommunicationPushConsumer.Handle(evt with { ConversationKind = "Meeting" }));
        Assert.Null(MobileCommunicationPushConsumer.Handle(evt with { TenantId = Guid.Empty }));
    }

    // N8-04: el comando del carril masivo encola un envío por destinatario; FCM no se llama aquí.
    [Fact]
    public async Task Chat_command_queues_one_push_per_recipient()
    {
        var (devices, recipients) = DevicesFor(2);
        var sender = new ScriptedPushSender(_ => PushErrorCodes.Unavailable);
        var queue = new RecordingPushDeliveryQueue();
        var dispatcher = new NotificationDispatcherHarness(sender, devices, pushQueue: queue).Dispatcher;

        await MobileCommunicationPushConsumer.Handle(
            MobileCommunicationPushConsumer.Handle(ChatTo(recipients))!,
            dispatcher,
            default
        );

        Assert.Empty(sender.Attempted);
        Assert.Equal(recipients, queue.Queued.Select(command => command.Request.RecipientUserId));
    }

    [Fact]
    public async Task Una_llamada_sin_proveedor_no_lanza()
    {
        var callee = Guid.NewGuid();
        var devices = new FakeDevices();
        devices.Add(callee, "token-callee");
        var sender = new ScriptedPushSender(_ => PushErrorCodes.NotConfigured);
        var logger = new ListLogger<CallStartedIntegrationEvent>();
        var dispatcher = new NotificationDispatcherHarness(sender, devices).Dispatcher;

        await MobileCommunicationPushConsumer.Handle(CallTo(callee), dispatcher, logger, TimeProvider.System, default);

        Assert.Equal(["token-callee"], sender.Attempted);
        Assert.Single(logger.Warnings);
    }

    [Fact]
    public async Task Una_llamada_con_fallo_transitorio_lanza_para_reintentar()
    {
        var callee = Guid.NewGuid();
        var devices = new FakeDevices();
        devices.Add(callee, "token-callee");
        var sender = new ScriptedPushSender(_ => PushErrorCodes.QuotaExceeded);
        var dispatcher = new NotificationDispatcherHarness(sender, devices).Dispatcher;

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            MobileCommunicationPushConsumer.Handle(
                CallTo(callee),
                dispatcher,
                new ListLogger<CallStartedIntegrationEvent>(),
                TimeProvider.System,
                default
            )
        );

        Assert.Equal(PushErrorCodes.QuotaExceeded, error.Message);
    }

    [Fact]
    public async Task Un_mensaje_del_chat_de_una_reunion_no_manda_push()
    {
        var (devices, recipients) = DevicesFor(2);
        var sender = new ScriptedPushSender(_ => null);
        var dispatcher = new NotificationDispatcherHarness(sender, devices).Dispatcher;

        await ChatPushLane.RunAsync(
            ChatTo(recipients) with
            {
                ConversationKind = "Meeting",
                MeetingId = Guid.NewGuid(),
            },
            dispatcher
        );

        Assert.Empty(sender.Attempted);
    }

    [Fact]
    public async Task Un_mensaje_sin_tipo_ni_lista_de_push_sigue_avisando_a_todos()
    {
        var (devices, recipients) = DevicesFor(2);
        var sender = new ScriptedPushSender(_ => null);
        var dispatcher = new NotificationDispatcherHarness(sender, devices).Dispatcher;

        await ChatPushLane.RunAsync(ChatTo(recipients), dispatcher);

        Assert.Equal(2, sender.Attempted.Count);
    }

    [Fact]
    public async Task La_lista_de_push_limita_a_quien_se_avisa()
    {
        var (devices, recipients) = DevicesFor(3);
        var sender = new ScriptedPushSender(_ => null);
        var dispatcher = new NotificationDispatcherHarness(sender, devices).Dispatcher;

        await ChatPushLane.RunAsync(ChatTo(recipients) with { PushRecipientUserIds = [recipients[1]] }, dispatcher);

        Assert.Equal([$"token-{recipients[1]:N}"], sender.Attempted);
    }

    [Fact]
    public async Task Una_lista_de_push_vacia_no_avisa_a_nadie()
    {
        var (devices, recipients) = DevicesFor(2);
        var sender = new ScriptedPushSender(_ => null);
        var dispatcher = new NotificationDispatcherHarness(sender, devices).Dispatcher;

        await ChatPushLane.RunAsync(ChatTo(recipients) with { PushRecipientUserIds = [] }, dispatcher);

        Assert.Empty(sender.Attempted);
    }

    private static (FakeDevices Devices, Guid[] Recipients) DevicesFor(int count)
    {
        var recipients = Enumerable.Range(0, count).Select(_ => Guid.NewGuid()).ToArray();
        var devices = new FakeDevices();
        foreach (var userId in recipients)
            devices.Add(userId, $"token-{userId:N}");
        return (devices, recipients);
    }

    private static ChatMessageSentIntegrationEvent ChatTo(IReadOnlyList<Guid> recipients) =>
        new()
        {
            TenantId = TenantId,
            ConversationId = Guid.NewGuid(),
            MessageId = Guid.NewGuid(),
            SenderId = Guid.NewGuid(),
            Kind = "Text",
            RecipientUserIds = [.. recipients],
            SentAtUtc = DateTime.UtcNow,
        };

    private static CallStartedIntegrationEvent CallTo(Guid callee) =>
        new()
        {
            TenantId = TenantId,
            CallId = Guid.NewGuid(),
            CallerUserId = Guid.NewGuid(),
            CalleeUserId = callee,
            Kind = "Audio",
            RingingAtUtc = DateTime.UtcNow,
        };

    // Devuelve el código de fallo por token, o null si sale bien.
    private sealed class ScriptedPushSender(Func<string, string?> failureFor) : IPushSender
    {
        public List<string> Attempted { get; } = [];

        public Task<Result> SendAsync(PushMessage message, CancellationToken ct = default)
        {
            Attempted.Add(message.Token);
            return Task.FromResult(
                failureFor(message.Token) is { } code
                    ? Result.Failure(new Error(code, "scripted failure"))
                    : Result.Success()
            );
        }
    }

    private sealed class FakeDevices : IPushDeviceTokenRepository
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

        public Task AddAsync(PushDeviceToken token, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<PushDeviceToken?> FindByTokenAsync(Guid tenantId, string token, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<PushDeviceToken?> GetAsync(Guid tenantId, Guid id, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task RevokeAsync(Guid tenantId, Guid id, CancellationToken ct = default) => Task.CompletedTask;

        public Task<int> RevokeAllActiveForUserAsync(Guid tenantId, Guid userId, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }

    private sealed class ListLogger<T> : ILogger<T>
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
