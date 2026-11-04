using BuildingBlocks.Common;
using BuildingBlocks.Messaging.EmailIntegrationEvents;
using BuildingBlocks.Messaging.NotificationIntegrationEvents;
using BuildingBlocks.Messaging.TenantIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Application.Consumers;
using TaxVision.Notification.Domain.Catalog;
using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Domain.Preferences;
using TaxVision.Notification.Tests.TestDoubles;

namespace TaxVision.Notification.Tests;

/// <summary>
/// Aviso de la revisión anual del WISP (F4): responsable de seguridad + <c>settings.manage</c> sin repetidos,
/// correo por Scribe/Postmaster, campana y push con destino a Office settings → Compliance.
/// </summary>
public sealed class OfficeWispReviewDueConsumerTests
{
    private static readonly Guid TenantId = Guid.Parse("8a0d8f8e-3f7e-4c55-9d3a-5b2f1c0e7a11");
    private static readonly Guid Coordinator = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid Admin = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid CoAdmin = Guid.Parse("33333333-3333-4333-8333-333333333333");
    private static readonly DateOnly DueOn = new(2026, 11, 30);

    [Fact]
    public async Task Avisa_al_responsable_y_a_los_admins_sin_repetir_al_que_es_ambas_cosas()
    {
        // El responsable también tiene settings.manage: recibe un solo aviso por canal.
        var harness = new Harness(active: [Coordinator, Admin, CoAdmin], admins: [Admin, Coordinator, CoAdmin]);

        await harness.HandleAsync(Event(OfficeWispReviewStages.DueSoon, Coordinator));

        var expected = new[] { Coordinator, Admin, CoAdmin };
        Assert.Equal(expected.OrderBy(x => x), harness.InAppRows.Select(row => row.RecipientUserId).OrderBy(x => x));
        Assert.Equal(
            expected.Select(Harness.EmailOf).OrderBy(x => x),
            harness.Queued.Select(email => email.To).OrderBy(x => x)
        );
        Assert.Equal(3, harness.Pushes.Count);
        Assert.Single(harness.Scribe.Calls);
    }

    [Fact]
    public async Task El_responsable_inactivo_se_omite_y_los_admins_reciben_igual()
    {
        var harness = new Harness(active: [Admin], admins: [Admin]);

        await harness.HandleAsync(Event(OfficeWispReviewStages.Overdue, Coordinator));

        Assert.Equal(new[] { Admin }, harness.InAppRows.Select(row => row.RecipientUserId));
        Assert.Equal(new[] { Harness.EmailOf(Admin) }, harness.Queued.Select(email => email.To));
    }

    [Fact]
    public async Task Sin_responsable_designado_avisa_solo_a_settings_manage()
    {
        var harness = new Harness(active: [Admin, CoAdmin], admins: [Admin, CoAdmin]);

        await harness.HandleAsync(Event(OfficeWispReviewStages.DueSoon, coordinator: null));

        Assert.Equal(
            new[] { Admin, CoAdmin }.OrderBy(x => x),
            harness.InAppRows.Select(row => row.RecipientUserId).OrderBy(x => x)
        );
        Assert.DoesNotContain(harness.Resolver.Audiences, audience => audience is Participants);
        var byPermission = Assert.Single(harness.Resolver.Audiences.OfType<ByPermission>());
        Assert.Equal("settings.manage", byPermission.PermissionCode);
    }

    [Fact]
    public async Task Due_soon_dice_la_fecha_y_lleva_destino_a_Compliance()
    {
        var harness = new Harness(active: [Admin], admins: [Admin], host: "manfer.taxoffix.io");
        var evt = Event(OfficeWispReviewStages.DueSoon, coordinator: null);

        await harness.HandleAsync(evt);

        var row = Assert.Single(harness.InAppRows);
        Assert.Equal(NotificationKinds.TenantWispReviewDue, row.Kind);
        Assert.Equal("Security", row.Category);
        Assert.Equal("compliance_settings", row.ResourceType);
        Assert.Equal(TenantId.ToString(), row.ResourceId);
        Assert.Equal("compliance", row.ResourceContext["section"]);
        Assert.Equal("Your security plan review is due November 30, 2026", row.FallbackTitle);
        Assert.Contains("FTC Safeguards Rule", row.FallbackBody);
        Assert.Contains("Office settings → Compliance", row.FallbackBody);
        Assert.Equal($"tenant.wisp_review_due:{TenantId:N}:20261130:DueSoon", row.GroupKey);

        var variables = Assert.Single(harness.Scribe.Calls);
        Assert.Equal(OfficeWispReviewDueConsumer.EmailEventKey, variables.EventKey);
        Assert.False((bool)variables.Variables["is_overdue"]!);
        Assert.Equal("November 30, 2026", variables.Variables["due_date"]);
        Assert.Equal("https://manfer.taxoffix.io/company/settings#compliance", variables.Variables["settings_url"]);

        var email = Assert.Single(harness.Queued);
        Assert.Equal(evt.EventId, email.RelatedEventId);
        Assert.Equal(OfficeWispReviewDueConsumer.EmailEventKey, email.TemplateKey);

        var push = Assert.Single(harness.Pushes);
        Assert.Equal("https://manfer.taxoffix.io/company/settings#compliance", push.Link);
        Assert.Equal(row.FallbackTitle, push.Title);
    }

    [Fact]
    public async Task Overdue_dice_que_vencio_y_agrupa_aparte_de_due_soon()
    {
        var harness = new Harness(active: [Admin], admins: [Admin]);

        await harness.HandleAsync(Event(OfficeWispReviewStages.Overdue, coordinator: null));

        var row = Assert.Single(harness.InAppRows);
        Assert.Equal("Your security plan review is overdue", row.FallbackTitle);
        Assert.Contains("It was due November 30, 2026.", row.FallbackBody);
        Assert.EndsWith(":Overdue", row.GroupKey);
        Assert.True((bool)Assert.Single(harness.Scribe.Calls).Variables["is_overdue"]!);
        // Sin host resuelto el enlace cae al base fijo del CRM.
        Assert.Equal("http://localhost:4200/company/settings#compliance", harness.Pushes[0].Link);
    }

    [Fact]
    public async Task Un_reintento_del_mismo_evento_no_repite_correo_ni_push()
    {
        var harness = new Harness(active: [Coordinator, Admin], admins: [Admin]);
        var evt = Event(OfficeWispReviewStages.DueSoon, Coordinator);

        await harness.HandleAsync(evt);
        await harness.HandleAsync(evt);

        // Correo: el consumer lo pide dos veces, pero el gateway dedupa por evento + destinatario.
        Assert.Equal(4, harness.Queued.Count);
        Assert.Equal(2, harness.Postmaster.Count);
        Assert.Equal(2, harness.Postmaster.Select(email => email.To).Distinct().Count());
        Assert.Equal(2, harness.Pushes.Count);
        // La campana se republica con la misma clave de grupo: Communication actualiza la fila sin leer.
        Assert.Single(harness.InAppRows.Select(row => row.GroupKey).Distinct());
    }

    [Fact]
    public async Task Si_Scribe_falla_no_sale_nada_y_el_mensaje_se_reintenta()
    {
        var harness = new Harness(active: [Admin], admins: [Admin], scribeFails: true);

        await Assert.ThrowsAsync<ScribeRenderFailedException>(() =>
            harness.HandleAsync(Event(OfficeWispReviewStages.DueSoon, coordinator: null))
        );

        Assert.Empty(harness.InAppRows);
        Assert.Empty(harness.Pushes);
        Assert.Empty(harness.Postmaster);
    }

    [Fact]
    public async Task Sin_direccion_de_correo_se_omite_ese_canal_pero_llega_la_campana()
    {
        var harness = new Harness(active: [Admin], admins: [Admin], withEmails: false);

        await harness.HandleAsync(Event(OfficeWispReviewStages.DueSoon, coordinator: null));

        Assert.Empty(harness.Scribe.Calls);
        Assert.Empty(harness.Queued);
        Assert.Single(harness.InAppRows);
    }

    [Fact]
    public async Task Una_etapa_desconocida_no_avisa()
    {
        var harness = new Harness(active: [Admin], admins: [Admin]);

        await harness.HandleAsync(Event("Later", coordinator: null));

        Assert.Empty(harness.Resolver.Audiences);
        Assert.Empty(harness.InAppRows);
    }

    [Fact]
    public void El_catalogo_lo_declara_obligatorio_en_campana_y_correo_con_push_opcional()
    {
        var entry = NotificationCatalog.GetRequired(NotificationKinds.TenantWispReviewDue);

        Assert.Equal(NotificationCatalogCategory.Security, entry.Category);
        Assert.Equal(NotificationResourceType.ComplianceSettings, entry.ResourceType);
        Assert.True(entry.IsMandatoryFor(NotificationChannel.InApp));
        Assert.True(entry.IsMandatoryFor(NotificationChannel.Email));
        Assert.False(entry.IsMandatoryFor(NotificationChannel.Push));
        Assert.Contains(NotificationChannel.Push, entry.AllowedChannels);
    }

    private static OfficeWispReviewDueIntegrationEvent Event(string stage, Guid? coordinator) =>
        new()
        {
            TenantId = TenantId,
            CorrelationId = "corr-wisp",
            OfficeTenantId = TenantId,
            DueOn = DueOn,
            Stage = stage,
            SecurityCoordinatorUserId = coordinator,
        };

    private sealed class Harness
    {
        private readonly NotificationDispatcherHarness _dispatcher;
        private readonly MemoryLogRepository _logs = new();
        private readonly RecordingPushSender _push = new();
        private readonly EventBasedEmailDispatchGateway _gateway;
        private readonly CapturingIntegrationEventPublisher _gatewayPublisher = new();
        private readonly bool _withEmails;
        private readonly ITenantHostResolver _hosts;

        public Harness(
            IReadOnlyList<Guid> active,
            IReadOnlyList<Guid> admins,
            string? host = null,
            bool scribeFails = false,
            bool withEmails = true
        )
        {
            Resolver = new FakeResolver(active, admins);
            Scribe = new RecordingScribe(scribeFails);
            _withEmails = withEmails;
            _hosts = new FakeTenantHostResolver(host);
            var devices = new FakeDevices([Coordinator, Admin, CoAdmin]);
            _dispatcher = new NotificationDispatcherHarness(pushSender: _push, pushDeviceTokens: devices, logs: _logs);
            _gateway = new EventBasedEmailDispatchGateway(
                _gatewayPublisher,
                _logs,
                new NoOpUnitOfWork(),
                NullLogger<EventBasedEmailDispatchGateway>.Instance
            );
            Queued = new List<EmailDispatchRequest>();
        }

        public FakeResolver Resolver { get; }
        public RecordingScribe Scribe { get; }
        public List<EmailDispatchRequest> Queued { get; }

        public IReadOnlyList<InAppNotificationRequestedIntegrationEvent> InAppRows =>
            _dispatcher.Publisher.Published.OfType<InAppNotificationRequestedIntegrationEvent>().ToArray();

        public IReadOnlyList<PushMessage> Pushes => _push.Sent;

        public IReadOnlyList<NotificationsEmailSendRequestedIntegrationEvent> Postmaster =>
            _gatewayPublisher.Published.OfType<NotificationsEmailSendRequestedIntegrationEvent>().ToArray();

        public static string EmailOf(Guid userId) => $"{userId:N}@office.test";

        public Task HandleAsync(OfficeWispReviewDueIntegrationEvent evt) =>
            OfficeWispReviewDueConsumer.Handle(
                evt,
                _dispatcher.Dispatcher,
                Resolver,
                new FakeEmails(_withEmails),
                new AllowAllPreferences(),
                new RecordingGateway(_gateway, Queued),
                Scribe,
                _hosts,
                Options.Create(new PortalOptions()),
                new TestCorrelationContext(),
                NullLogger<OfficeWispReviewDueIntegrationEvent>.Instance,
                CancellationToken.None
            );
    }

    /// <summary>Participants filtra por activos (como la proyección real); settings.manage devuelve los admins.</summary>
    private sealed class FakeResolver(IReadOnlyList<Guid> active, IReadOnlyList<Guid> admins) : IRecipientResolver
    {
        public List<NotificationAudience> Audiences { get; } = [];

        public Task<IReadOnlyList<Guid>> ResolveAsync(ByPermission audience, CancellationToken ct = default) =>
            Task.FromResult(admins);

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
                ByPermission { PermissionCode: "settings.manage" } => admins.Where(active.Contains).ToArray(),
                _ => [],
            };
            return Task.FromResult(Result.Success(ids));
        }
    }

    private sealed class FakeEmails(bool withEmails) : IUserEmailResolver
    {
        public Task<string?> ResolveAsync(Guid tenantId, Guid userId, CancellationToken ct = default) =>
            Task.FromResult(withEmails ? Harness.EmailOf(userId) : null);
    }

    private sealed class RecordingScribe(bool fails) : IScribeRenderClient
    {
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

    /// <summary>Envuelve el gateway real (idempotencia incluida) y registra lo que el consumer le pidió.</summary>
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

    private sealed class FakeDevices(IReadOnlyList<Guid> users) : IPushDeviceTokenRepository
    {
        private readonly List<PushDeviceToken> _devices = users
            .Select(user => PushDeviceToken.Register(TenantId, user, PushPlatform.Fcm, $"token-{user:N}", null).Value)
            .ToList();

        public Task AddAsync(PushDeviceToken token, CancellationToken ct = default) => Task.CompletedTask;

        public Task<PushDeviceToken?> FindByTokenAsync(Guid tenantId, string token, CancellationToken ct = default) =>
            Task.FromResult(_devices.FirstOrDefault(d => d.Token == token));

        public Task<PushDeviceToken?> GetAsync(Guid tenantId, Guid id, CancellationToken ct = default) =>
            Task.FromResult(_devices.FirstOrDefault(d => d.Id == id));

        public Task<IReadOnlyList<PushDeviceToken>> ListActiveForUserAsync(
            Guid tenantId,
            Guid userId,
            CancellationToken ct = default
        ) => Task.FromResult<IReadOnlyList<PushDeviceToken>>(_devices.Where(d => d.UserId == userId).ToList());

        public Task RevokeAsync(Guid tenantId, Guid id, CancellationToken ct = default) => Task.CompletedTask;
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
