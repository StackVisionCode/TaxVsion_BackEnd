using BuildingBlocks.Common;
using BuildingBlocks.Messaging.ReminderIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Notification.Domain.Directory;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Application.Consumers;
using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Domain.Preferences;

namespace TaxVision.Notification.Tests;

/// <summary>
/// Reminder Fase 8 — la entrega del recordatorio. Lo que se prueba acá es lo que un E2E feliz no
/// muestra: que el gate de preferencias apague <b>los dos</b> canales, y que el cuerpo por defecto
/// use la zona del usuario y no UTC (en el aviso de un recordatorio la hora es todo el contenido).
/// </summary>
public sealed class ReminderDueConsumerTests
{
    private static readonly Guid TenantId = Guid.Parse("d4879234-7370-4b58-b49c-094bd7c04847");
    private static readonly Guid UserId = Guid.Parse("2b91f0c4-1111-4222-8333-444455556666");

    [Fact]
    public async Task Recordatorio_vencido_registra_in_app_manda_push_y_encola_el_correo()
    {
        var harness = new Harness(preferencesEnabled: true, withActiveDevice: true);
        harness.Directory.Seed(TenantId, UserId, "perez@example.com");

        await Handle(harness, Due());

        Assert.Single(harness.Logs.Logs, log => log.Channel == NotificationChannel.InApp);
        Assert.Single(harness.PushSender.Sent);
        Assert.All(harness.Logs.Logs, log => Assert.Equal("reminder.due", log.TemplateKey));

        // Fase 10 — el tercer canal. La dirección salió del directorio local, sin pegarle a Auth.
        var email = Assert.Single(harness.EmailGateway.Queued);
        Assert.Equal("perez@example.com", email.To);
        Assert.Equal(0, harness.ContactClient.Calls);
    }

    /// <summary>
    /// El caso que hace usable la proyección: el usuario existía antes de que la tabla existiera, así
    /// que no hay fila local. Sin la recuperación pull, el correo solo funcionaría para los usuarios
    /// registrados después de la Fase 10 — y un correo que anda solo para los nuevos es peor que no
    /// tener correo, porque parece que funciona.
    /// </summary>
    [Fact]
    public async Task Sin_fila_local_el_correo_sale_igual_recuperando_la_direccion_de_Auth()
    {
        var harness = new Harness(preferencesEnabled: true, withActiveDevice: true, pullEmail: "recuperado@example.com");

        await Handle(harness, Due());

        var email = Assert.Single(harness.EmailGateway.Queued);
        Assert.Equal("recuperado@example.com", email.To);
        Assert.Equal(1, harness.ContactClient.Calls);

        // Y queda persistida: el siguiente recordatorio de este usuario no vuelve a pegarle a Auth.
        Assert.Single(harness.Directory.Entries);
    }

    /// <summary>
    /// Ni directorio ni Auth: el correo se omite, pero in-app y push igual salieron. Quedarse sin
    /// avisar por nada sería peor que avisar por dos canales de tres.
    /// </summary>
    [Fact]
    public async Task Sin_direccion_resoluble_se_omite_el_correo_pero_los_otros_canales_llegan()
    {
        var harness = new Harness(preferencesEnabled: true, withActiveDevice: true);

        await Handle(harness, Due());

        Assert.Empty(harness.EmailGateway.Queued);
        Assert.Single(harness.Logs.Logs, log => log.Channel == NotificationChannel.InApp);
        Assert.Single(harness.PushSender.Sent);
    }

    /// <summary>
    /// El gate vive en el dispatcher, así que apagar la categoría apaga in-app y push a la vez sin
    /// que el consumer sepa nada. Si alguien mueve el gate al consumer, este test lo delata.
    /// </summary>
    [Fact]
    public async Task Con_la_categoria_Reminders_apagada_no_llega_por_ningun_canal()
    {
        var harness = new Harness(preferencesEnabled: false, withActiveDevice: true);

        await Handle(harness, Due());

        Assert.Empty(harness.Logs.Logs);
        Assert.Empty(harness.PushSender.Sent);
        Assert.Equal(NotificationCategory.Reminders, harness.Preferences.LastAskedCategory);
    }

    [Fact]
    public async Task Un_recordatorio_pospuesto_lo_dice_en_el_titulo()
    {
        var harness = new Harness(preferencesEnabled: true, withActiveDevice: true);

        await Handle(harness, Due() with { SnoozeCount = 2 });

        Assert.Contains("pospuesto 2", harness.PushSender.Sent[0].Title);
    }

    /// <summary>
    /// Sin cuerpo propio, el aviso cae al ancla — y tiene que mostrarla en la zona del usuario.
    /// El evento la trae en UTC: America/Santo_Domingo es UTC-4, así que 18:00Z se ve como 14:00.
    /// </summary>
    [Fact]
    public async Task Sin_cuerpo_usa_la_hora_del_ancla_en_la_zona_del_usuario()
    {
        var harness = new Harness(preferencesEnabled: true, withActiveDevice: true);
        var anchorUtc = new DateTime(2026, 8, 20, 18, 0, 0, DateTimeKind.Utc);

        await Handle(harness, Due() with { Body = null, AnchorAtUtc = anchorUtc });

        Assert.Contains("14:00", harness.PushSender.Sent[0].Body);
    }

    private static Task Handle(Harness harness, ReminderDueIntegrationEvent evt) =>
        ReminderDueConsumer.Handle(
            evt,
            harness.Dispatcher,
            harness.EmailResolver,
            harness.EmailGateway,
            new StubScribeRenderClient(),
            Options.Create(new PortalOptions()),
            new NoOpCorrelationContext(),
            CancellationToken.None
        );

    private static ReminderDueIntegrationEvent Due() =>
        new()
        {
            TenantId = TenantId,
            CorrelationId = "test",
            ReminderId = Guid.NewGuid(),
            UserId = UserId,
            Category = "General",
            Title = "Llamar a Pérez",
            Body = "Antes de que cierre",
            TimeZoneId = "America/Santo_Domingo",
            FiredAtUtc = DateTime.UtcNow,
            SnoozeCount = 0,
        };

    // ------------------------------------------------------------------
    // Fakes hand-rolled, mismo estilo que NotificationDispatcherPushTests.
    // ------------------------------------------------------------------

    private sealed class Harness
    {
        internal RecordingPushSender PushSender { get; } = new();
        internal RecordingLogRepository Logs { get; } = new();
        internal SwitchablePreferenceRepository Preferences { get; }
        internal NotificationDispatcher Dispatcher { get; }
        internal RecordingEmailGateway EmailGateway { get; } = new();
        internal FakeEmailDirectoryRepository Directory { get; } = new();
        internal SwitchableContactSnapshotClient ContactClient { get; }
        internal UserEmailResolver EmailResolver { get; }

        internal Harness(bool preferencesEnabled, bool withActiveDevice, string? pullEmail = null)
        {
            Preferences = new SwitchablePreferenceRepository(preferencesEnabled);
            ContactClient = new SwitchableContactSnapshotClient(pullEmail);
            EmailResolver = new UserEmailResolver(
                Directory,
                ContactClient,
                new NoOpUnitOfWork(),
                NullLogger<UserEmailResolver>.Instance
            );
            var devices = new FakeDeviceRepository();
            if (withActiveDevice)
                devices.AddActiveDevice(TenantId, UserId, "token-1");

            Dispatcher = new NotificationDispatcher(
                new NoOpSmsSender(),
                PushSender,
                devices,
                Logs,
                Preferences,
                new NoOpUnitOfWork(),
                NullLogger<NotificationDispatcher>.Instance
            );
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
            CancellationToken ct = default
        ) => Task.FromResult<NotificationLog?>(null);
    }

    private sealed class SwitchablePreferenceRepository(bool enabled) : IUserNotificationPreferenceRepository
    {
        public NotificationCategory? LastAskedCategory { get; private set; }

        public Task<bool> IsEnabledAsync(
            Guid tenantId,
            Guid userId,
            NotificationCategory category,
            NotificationChannel channel,
            CancellationToken ct = default
        )
        {
            LastAskedCategory = category;
            return Task.FromResult(enabled);
        }

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

    private sealed class RecordingEmailGateway : IEmailDispatchGateway
    {
        public List<EmailDispatchRequest> Queued { get; } = [];

        public Task<EmailDispatchResult> QueueEmailAsync(EmailDispatchRequest request, CancellationToken ct = default)
        {
            Queued.Add(request);
            return Task.FromResult(
                new EmailDispatchResult(Guid.NewGuid(), Guid.NewGuid(), NotificationDispatchAttemptStatus.Sent, null, null)
            );
        }
    }

    private sealed class StubScribeRenderClient : IScribeRenderClient
    {
        public Task<Result<ScribeRenderedEmail>> RenderAsync(
            string eventKey,
            Guid tenantId,
            IReadOnlyDictionary<string, object?> variables,
            CancellationToken ct = default
        ) =>
            Task.FromResult(
                Result.Success(new ScribeRenderedEmail($"[{eventKey}] {variables["title"]}", "<p>html</p>", "text"))
            );
    }

    private sealed class FakeEmailDirectoryRepository : IUserEmailDirectoryRepository
    {
        public List<UserEmailDirectoryEntry> Entries { get; } = [];

        internal void Seed(Guid tenantId, Guid userId, string email) =>
            Entries.Add(UserEmailDirectoryEntry.Create(tenantId, userId, email));

        public Task<UserEmailDirectoryEntry?> FindAsync(Guid tenantId, Guid userId, CancellationToken ct = default) =>
            Task.FromResult(Entries.FirstOrDefault(e => e.TenantId == tenantId && e.UserId == userId));

        public Task AddAsync(UserEmailDirectoryEntry entry, CancellationToken ct = default)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }

    private sealed class SwitchableContactSnapshotClient(string? email) : IUserContactSnapshotClient
    {
        public int Calls { get; private set; }

        public Task<RemoteUserContact?> FetchContactAsync(Guid tenantId, Guid userId, CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult(email is null ? null : new RemoteUserContact(email, IsActive: true));
        }
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
