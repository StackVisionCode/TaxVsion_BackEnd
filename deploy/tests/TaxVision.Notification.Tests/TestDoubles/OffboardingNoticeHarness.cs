using BuildingBlocks.Common;
using BuildingBlocks.Messaging.EmailIntegrationEvents;
using BuildingBlocks.Messaging.NotificationIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Domain.Preferences;

namespace TaxVision.Notification.Tests.TestDoubles;

/// <summary>
/// Arma <see cref="OffboardingNoticeSender"/> con dobles de los avisos del retiro: resolvedor por permiso y
/// activos, correo por Scribe y el gateway real (idempotencia incluida), campana y push del dispatcher real.
/// </summary>
internal sealed class OffboardingNoticeHarness
{
    public static readonly Guid TenantId = Guid.Parse("8a0d8f8e-3f7e-4c55-9d3a-5b2f1c0e7a11");

    private readonly NotificationDispatcherHarness _dispatcher;
    private readonly RecordingPushSender _push = new();
    private readonly CapturingIntegrationEventPublisher _gatewayPublisher = new();

    public OffboardingNoticeHarness(
        IReadOnlyList<Guid> active,
        IReadOnlyDictionary<string, IReadOnlyList<Guid>>? byPermission = null,
        string? host = null,
        bool scribeFails = false,
        IReadOnlyCollection<Guid>? emailDisabled = null,
        IReadOnlyCollection<Guid>? inactiveInDirectory = null
    )
    {
        Resolver = new FakeResolver(active, byPermission ?? new Dictionary<string, IReadOnlyList<Guid>>());
        Scribe = new RecordingScribe(scribeFails);
        var logs = new MemoryLogRepository();
        var preferences = new EmailOptOutPreferences(emailDisabled ?? []);
        var directory = new InMemoryUserEmailDirectoryRepository();
        foreach (var userId in inactiveInDirectory ?? [])
            directory.Seed(TenantId, userId, EmailOf(userId), isActive: false);
        _dispatcher = new NotificationDispatcherHarness(
            pushSender: _push,
            pushDeviceTokens: new FakeDevices(),
            logs: logs,
            preferences: preferences,
            userDirectory: directory
        );
        var gateway = new EventBasedEmailDispatchGateway(
            _gatewayPublisher,
            logs,
            new NoOpUnitOfWork(),
            TestDeliveryGates.Open(),
            NullLogger<EventBasedEmailDispatchGateway>.Instance
        );
        Correlation = new TestCorrelationContext();
        Sender = new OffboardingNoticeSender(
            _dispatcher.Dispatcher,
            Resolver,
            new FakeEmails(inactiveInDirectory ?? []),
            preferences,
            new RecordingGateway(gateway, Queued),
            Scribe,
            new FakeTenantHostResolver(host),
            Options.Create(new PortalOptions()),
            Correlation
        );
    }

    public OffboardingNoticeSender Sender { get; }
    public ICorrelationContext Correlation { get; }
    public FakeResolver Resolver { get; }
    public RecordingScribe Scribe { get; }
    public List<EmailDispatchRequest> Queued { get; } = [];

    public IReadOnlyList<InAppNotificationRequestedIntegrationEvent> InAppRows =>
        _dispatcher.Publisher.Published.OfType<InAppNotificationRequestedIntegrationEvent>().ToArray();

    public IReadOnlyList<PushMessage> Pushes => _push.Sent;

    /// <summary>Lo que llegó a Postmaster tras la idempotencia del gateway.</summary>
    public IReadOnlyList<NotificationsEmailSendRequestedIntegrationEvent> Postmaster =>
        _gatewayPublisher.Published.OfType<NotificationsEmailSendRequestedIntegrationEvent>().ToArray();

    public IReadOnlyList<Guid> InAppRecipients => InAppRows.Select(row => row.RecipientUserId).ToArray();

    public static string EmailOf(Guid userId) => $"{userId:N}@office.test";

    /// <summary>Participants filtra por activos (como la proyección real); ByPermission devuelve lo configurado activo.</summary>
    internal sealed class FakeResolver(
        IReadOnlyList<Guid> active,
        IReadOnlyDictionary<string, IReadOnlyList<Guid>> byPermission
    ) : IRecipientResolver
    {
        public List<NotificationAudience> Audiences { get; } = [];

        public Task<IReadOnlyList<Guid>> ResolveAsync(ByPermission audience, CancellationToken ct = default) =>
            Task.FromResult(byPermission.TryGetValue(audience.PermissionCode, out var users) ? users : []);

        public Task<Result<IReadOnlyList<Guid>>> ResolveAsync(
            NotificationAudience audience,
            RecipientValidation validation,
            CancellationToken ct = default
        )
        {
            Audiences.Add(audience);
            IReadOnlyList<Guid> ids = audience switch
            {
                Participants participants => participants.UserIds.Where(active.Contains).ToArray(),
                ByPermission permission when byPermission.TryGetValue(permission.PermissionCode, out var users) => users
                    .Where(active.Contains)
                    .ToArray(),
                _ => [],
            };
            return Task.FromResult(Result.Success(ids));
        }
    }

    internal sealed class RecordingScribe(bool fails) : IScribeRenderClient
    {
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
        ) => TestDoubles.ScribeTestDefaults.TextUnsupported();

        public List<(string EventKey, IReadOnlyDictionary<string, object?> Variables)> Calls { get; } = [];

        public Task<Result<ScribeRenderedEmail>> RenderAsync(
            string eventKey,
            Guid tenantId,
            IReadOnlyDictionary<string, object?> variables,
            CancellationToken ct = default
        )
        {
            Calls.Add((eventKey, variables));
            return Task.FromResult(
                fails
                    ? Result.Failure<ScribeRenderedEmail>(new Error("Scribe.Down", "unavailable"))
                    : Result.Success(new ScribeRenderedEmail("Subject", "<p>html</p>", "text"))
            );
        }
    }

    // Como el resolvedor real: un usuario inactivo no tiene a dónde escribirle.
    private sealed class FakeEmails(IReadOnlyCollection<Guid> inactive) : IUserEmailResolver
    {
        public Task<string?> ResolveAsync(Guid tenantId, Guid userId, CancellationToken ct = default) =>
            Task.FromResult(inactive.Contains(userId) ? null : EmailOf(userId));
    }

    private sealed class RecordingGateway(IEmailDispatchGateway inner, List<EmailDispatchRequest> queued)
        : IEmailDispatchGateway
    {
        public async Task<EmailDispatchResult> QueueEmailAsync(
            EmailDispatchRequest request,
            CancellationToken ct = default
        )
        {
            queued.Add(request);
            return await inner.QueueEmailAsync(request, ct);
        }
    }

    // Apaga el correo para los usuarios dados; el resto de canales queda encendido.
    private sealed class EmailOptOutPreferences(IReadOnlyCollection<Guid> emailDisabled)
        : IUserNotificationPreferenceRepository
    {
        public Task<bool> IsEnabledAsync(
            Guid tenantId,
            Guid userId,
            NotificationCategory category,
            NotificationChannel channel,
            CancellationToken ct = default
        ) => Task.FromResult(!(channel == NotificationChannel.Email && emailDisabled.Contains(userId)));

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

    private sealed class MemoryLogRepository : INotificationLogRepository
    {
        private readonly List<NotificationLog> _logs = [];

        public Task AddAsync(NotificationLog log, CancellationToken ct = default)
        {
            _logs.Add(log);
            return Task.CompletedTask;
        }

        public Task<(IReadOnlyList<NotificationLog> Items, int TotalCount)> GetPagedAsync(
            Guid tenantId,
            NotificationStatus? status,
            int page,
            int size,
            CancellationToken ct = default
        ) => Task.FromResult<(IReadOnlyList<NotificationLog>, int)>((_logs, _logs.Count));

        public Task<NotificationLog?> GetByRelatedEventIdAsync(
            Guid tenantId,
            Guid relatedEventId,
            string templateKey,
            string recipient,
            CancellationToken ct = default
        ) =>
            Task.FromResult(
                _logs.FirstOrDefault(log =>
                    log.Channel == NotificationChannel.Email
                    && log.RelatedEventId == relatedEventId
                    && log.TemplateKey == templateKey
                    && string.Equals(log.Recipient, recipient, StringComparison.OrdinalIgnoreCase)
                )
            );

        public Task<NotificationLog?> GetByRelatedEventAsync(
            Guid tenantId,
            Guid relatedEventId,
            string kind,
            NotificationChannel channel,
            string recipient,
            CancellationToken ct = default
        ) =>
            Task.FromResult(
                _logs.LastOrDefault(log =>
                    log.Channel == channel
                    && log.RelatedEventId == relatedEventId
                    && log.Kind == kind
                    && log.Recipient == recipient
                )
            );
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

    // Todo usuario tiene un dispositivo: el push depende solo de los gates del dispatcher.
    private sealed class FakeDevices : IPushDeviceTokenRepository
    {
        public Task<int> RevokeAllActiveForUserAsync(Guid tenantId, Guid userId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task AddAsync(PushDeviceToken token, CancellationToken ct = default) => Task.CompletedTask;

        public Task<PushDeviceToken?> FindByTokenAsync(Guid tenantId, string token, CancellationToken ct = default) =>
            Task.FromResult<PushDeviceToken?>(null);

        public Task<PushDeviceToken?> GetAsync(Guid tenantId, Guid id, CancellationToken ct = default) =>
            Task.FromResult<PushDeviceToken?>(null);

        public Task<IReadOnlyList<PushDeviceToken>> ListActiveForUserAsync(
            Guid tenantId,
            Guid userId,
            CancellationToken ct = default
        ) =>
            Task.FromResult<IReadOnlyList<PushDeviceToken>>([
                PushDeviceToken.Register(tenantId, userId, PushPlatform.Fcm, $"token-{userId:N}", null).Value,
            ]);

        public Task RevokeAsync(Guid tenantId, Guid id, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class NoOpUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);
    }

    private sealed class TestCorrelationContext : ICorrelationContext
    {
        public string CorrelationId { get; private set; } = string.Empty;

        public void Set(string correlationId) => CorrelationId = correlationId;

        public IDisposable Push(string correlationId)
        {
            CorrelationId = correlationId;
            return new Scope();
        }

        private sealed class Scope : IDisposable
        {
            public void Dispose() { }
        }
    }
}
