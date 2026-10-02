using BuildingBlocks.Authorization;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CommunicationIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Application.Consumers.Communication;
using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Domain.Preferences;

namespace TaxVision.Notification.Tests;

public sealed class SupportOpenedConsumerTests
{
    private static readonly Guid OfficeTenantId = Guid.Parse("d4879234-7370-4b58-b49c-094bd7c04847");
    private static readonly Guid PlatformTenantId = Guid.Parse("8f58a521-4c25-4d91-9f4e-7ad5df14c001");
    private static readonly Guid AgentId = Guid.Parse("2cd3f306-ffe6-4a00-a0c4-83942777c3c6");

    [Fact]
    public async Task Ticket_abierto_notifica_a_los_agentes_del_tenant_plataforma()
    {
        var harness = new Harness([AgentId], withActiveDevice: true);

        await Handle(harness, Opened([AgentId]));

        Assert.Null(harness.Resolver.LastAudience);
        Assert.Single(harness.PushSender.Sent);
        Assert.All(harness.Logs.Logs, log => Assert.Equal(PlatformTenantId, log.TenantId));
        Assert.Contains(harness.Logs.Logs, log => log.Channel == NotificationChannel.InApp);
        Assert.Contains(harness.Logs.Logs, log => log.Channel == NotificationChannel.Push);
    }

    [Fact]
    public async Task Sin_agentes_resueltos_no_crea_notificaciones()
    {
        var harness = new Harness([], withActiveDevice: true);

        await Handle(harness, Opened());

        Assert.Empty(harness.Logs.Logs);
        Assert.Empty(harness.PushSender.Sent);
    }

    [Fact]
    public async Task Evento_legacy_sin_destinatarios_explicitos_usa_resolver_por_permiso()
    {
        var harness = new Harness([AgentId], withActiveDevice: true);

        await Handle(harness, Opened());

        Assert.Equal(
            new ByPermission(PlatformTenantId, CommunicationPermissions.SupportAgent),
            harness.Resolver.LastAudience
        );
        Assert.Single(harness.PushSender.Sent);
    }

    private static Task Handle(Harness harness, SupportOpenedIntegrationEvent evt) =>
        SupportOpenedConsumer.Handle(
            evt,
            harness.Dispatcher,
            harness.Resolver,
            new NoOpCorrelationContext(),
            CancellationToken.None
        );

    private static SupportOpenedIntegrationEvent Opened(IReadOnlyList<Guid>? supportRecipients = null) =>
        new()
        {
            TenantId = OfficeTenantId,
            AgentTenantId = PlatformTenantId,
            SupportRecipientUserIds = supportRecipients ?? [],
            CorrelationId = "corr-support",
            TicketId = Guid.NewGuid(),
            OpenedByUserId = Guid.NewGuid(),
            ConversationId = Guid.NewGuid(),
            Subject = "App Mobile",
            Category = "Technical",
            Priority = "High",
        };

    private sealed class Harness
    {
        internal RecordingPushSender PushSender { get; } = new();
        internal RecordingLogRepository Logs { get; } = new();
        internal FakeRecipientResolver Resolver { get; }
        internal NotificationDispatcher Dispatcher { get; }

        internal Harness(IReadOnlyList<Guid> recipients, bool withActiveDevice)
        {
            Resolver = new FakeRecipientResolver(recipients);
            var devices = new FakeDeviceRepository();
            if (withActiveDevice)
                devices.AddActiveDevice(PlatformTenantId, AgentId, "token-1");

            Dispatcher = new NotificationDispatcher(
                new NoOpSmsSender(),
                PushSender,
                devices,
                Logs,
                new AllowAllPreferences(),
                new NoOpUnitOfWork(),
                NullLogger<NotificationDispatcher>.Instance
            );
        }
    }

    private sealed class FakeRecipientResolver(IReadOnlyList<Guid> recipients) : IRecipientResolver
    {
        public ByPermission? LastAudience { get; private set; }

        public Task<IReadOnlyList<Guid>> ResolveAsync(ByPermission audience, CancellationToken ct = default)
        {
            LastAudience = audience;
            return Task.FromResult(recipients);
        }
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

    private sealed class FakeDeviceRepository : IPushDeviceTokenRepository
    {
        private readonly List<PushDeviceToken> _devices = [];

        public void AddActiveDevice(Guid tenantId, Guid userId, string token) =>
            _devices.Add(PushDeviceToken.Register(tenantId, userId, PushPlatform.Fcm, token, deviceId: null).Value);

        public Task AddAsync(PushDeviceToken token, CancellationToken ct = default)
        {
            _devices.Add(token);
            return Task.CompletedTask;
        }

        public Task<PushDeviceToken?> FindByTokenAsync(Guid tenantId, string token, CancellationToken ct = default) =>
            Task.FromResult(_devices.FirstOrDefault(d => d.TenantId == tenantId && d.Token == token));

        public Task<PushDeviceToken?> GetAsync(Guid tenantId, Guid id, CancellationToken ct = default) =>
            Task.FromResult(_devices.FirstOrDefault(d => d.TenantId == tenantId && d.Id == id));

        public Task<IReadOnlyList<PushDeviceToken>> ListActiveForUserAsync(
            Guid tenantId,
            Guid userId,
            CancellationToken ct = default
        ) =>
            Task.FromResult<IReadOnlyList<PushDeviceToken>>(
                _devices.Where(d => d.TenantId == tenantId && d.UserId == userId && d.IsActive).ToList()
            );

        public Task RevokeAsync(Guid tenantId, Guid id, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class RecordingLogRepository : INotificationLogRepository
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
    }

    private sealed class AllowAllPreferences : IUserNotificationPreferenceRepository
    {
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
        ) => Task.FromResult<IReadOnlyList<UserNotificationPreference>>([]);

        public Task AddAsync(UserNotificationPreference preference, CancellationToken ct = default) =>
            Task.CompletedTask;
    }

    private sealed class NoOpUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);
    }

    private sealed class NoOpSmsSender : ISmsSender
    {
        public Task<Result> SendAsync(string phoneNumber, string text, CancellationToken ct = default) =>
            Task.FromResult(Result.Success());
    }

    private sealed class NoOpCorrelationContext : ICorrelationContext
    {
        public string CorrelationId { get; private set; } = "test";

        public void Set(string correlationId) => CorrelationId = correlationId;

        public IDisposable Push(string correlationId) => new NoOpScope();

        private sealed class NoOpScope : IDisposable
        {
            public void Dispose() { }
        }
    }
}
