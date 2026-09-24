using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CommunicationIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Application.Consumers.Communication;
using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Domain.Preferences;

namespace TaxVision.Notification.Tests;

/// <summary>
/// La invitación a un meeting (evento de Communication) ahora manda correo real, no solo in-app. Lo que
/// importa verificar: que se renderice con el <c>join_link</c> ya armado por Communication (subdominio
/// correcto), que se encole al email del invitado, y que un invitado sin email siga recibiendo el in-app
/// sin encolar correo.
/// </summary>
public sealed class MeetingInvitationCreatedConsumerTests
{
    private static readonly Guid TenantId = Guid.Parse("d4879234-7370-4b58-b49c-094bd7c04847");
    private const string JoinUrl = "https://manfer.taxproffice.com/portal/client/meetings/accept/abc?token=t";

    [Fact]
    public async Task Con_email_renderiza_con_el_join_link_y_encola_el_correo_y_registra_in_app()
    {
        var harness = new Harness();

        await Handle(harness, Invitation("cliente@example.com"));

        var email = Assert.Single(harness.Gateway.Queued);
        Assert.Equal("cliente@example.com", email.To);
        Assert.Equal("communication.meeting.invitation", email.TemplateKey);
        Assert.Equal("communication.meeting.invitation_created.v1", harness.Render.LastEventKey);
        Assert.Equal(JoinUrl, harness.Render.LastVariables["join_link"]);
        Assert.Single(harness.Logs.Logs, log => log.Channel == NotificationChannel.InApp);
    }

    [Fact]
    public async Task Sin_email_registra_in_app_pero_no_encola_correo()
    {
        var harness = new Harness();

        await Handle(harness, Invitation(email: null));

        Assert.Empty(harness.Gateway.Queued);
        Assert.Equal(0, harness.Render.Calls);
        Assert.Single(harness.Logs.Logs, log => log.Channel == NotificationChannel.InApp);
    }

    private static Task Handle(Harness harness, MeetingInvitationCreatedIntegrationEvent evt) =>
        MeetingInvitationCreatedConsumer.Handle(
            evt,
            harness.Dispatcher,
            harness.Gateway,
            harness.Render,
            Options.Create(new PortalOptions()),
            new NoOpCorrelationContext(),
            NullLogger<MeetingInvitationCreatedLog>.Instance,
            CancellationToken.None
        );

    private static MeetingInvitationCreatedIntegrationEvent Invitation(string? email) =>
        new()
        {
            TenantId = TenantId,
            CorrelationId = "test",
            InvitationId = Guid.NewGuid(),
            MeetingId = Guid.NewGuid(),
            InviteeKind = "Customer",
            InviteeUserId = Guid.NewGuid(),
            InviteeEmail = email,
            InviteeName = "Cliente",
            TokenHash = "hash",
            ExpiresAtUtc = new DateTime(2026, 9, 24, 12, 0, 0, DateTimeKind.Utc),
            JoinUrl = JoinUrl,
        };

    private sealed class Harness
    {
        internal RecordingLogRepository Logs { get; } = new();
        internal RecordingEmailGateway Gateway { get; } = new();
        internal RecordingRenderClient Render { get; } = new();
        internal NotificationDispatcher Dispatcher { get; }

        internal Harness()
        {
            Dispatcher = new NotificationDispatcher(
                new NoOpSmsSender(),
                new NoOpPushSender(),
                new NoOpDeviceRepository(),
                Logs,
                new AllowAllPreferences(),
                new NoOpUnitOfWork(),
                NullLogger<NotificationDispatcher>.Instance
            );
        }
    }

    private sealed class RecordingRenderClient : IScribeRenderClient
    {
        public IReadOnlyDictionary<string, object?> LastVariables { get; private set; } =
            new Dictionary<string, object?>();
        public string? LastEventKey { get; private set; }
        public int Calls { get; private set; }

        public Task<Result<ScribeRenderedEmail>> RenderAsync(
            string eventKey,
            Guid tenantId,
            IReadOnlyDictionary<string, object?> variables,
            CancellationToken ct = default
        )
        {
            Calls++;
            LastEventKey = eventKey;
            LastVariables = variables;
            return Task.FromResult(Result.Success(new ScribeRenderedEmail("subject", "<p>html</p>", "text")));
        }
    }

    private sealed class RecordingEmailGateway : IEmailDispatchGateway
    {
        public List<EmailDispatchRequest> Queued { get; } = [];

        public Task<EmailDispatchResult> QueueEmailAsync(EmailDispatchRequest request, CancellationToken ct = default)
        {
            Queued.Add(request);
            return Task.FromResult(
                new EmailDispatchResult(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    NotificationDispatchAttemptStatus.Sent,
                    null,
                    null
                )
            );
        }
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

    private sealed class NoOpDeviceRepository : IPushDeviceTokenRepository
    {
        public Task AddAsync(PushDeviceToken token, CancellationToken ct = default) => Task.CompletedTask;

        public Task<PushDeviceToken?> FindByTokenAsync(Guid tenantId, string token, CancellationToken ct = default) =>
            Task.FromResult<PushDeviceToken?>(null);

        public Task<PushDeviceToken?> GetAsync(Guid tenantId, Guid id, CancellationToken ct = default) =>
            Task.FromResult<PushDeviceToken?>(null);

        public Task<IReadOnlyList<PushDeviceToken>> ListActiveForUserAsync(
            Guid tenantId,
            Guid userId,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<PushDeviceToken>>([]);

        public Task RevokeAsync(Guid tenantId, Guid id, CancellationToken ct = default) => Task.CompletedTask;
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

    private sealed class NoOpPushSender : IPushSender
    {
        public Task<Result> SendAsync(PushMessage message, CancellationToken ct = default) =>
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
