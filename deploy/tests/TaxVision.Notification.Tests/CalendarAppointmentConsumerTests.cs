using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CalendarIntegrationEvents;
using BuildingBlocks.Persistence;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Application.Consumers.Calendar;
using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Domain.Preferences;
using TaxVision.Notification.Tests.TestDoubles;

namespace TaxVision.Notification.Tests;

/// <summary>
/// La invitación de Calendar. Lo que se prueba es lo que un E2E feliz no muestra: que la hora salga en
/// la zona de la cita y no en UTC, que apagar la preferencia calle sólo al que la apagó, y que a un
/// cliente invitado —sin cuenta, y por tanto sin preferencia— se le siga escribiendo.
/// </summary>
public sealed class CalendarAppointmentConsumerTests
{
    private static readonly Guid TenantId = Guid.Parse("d4879234-7370-4b58-b49c-094bd7c04847");
    private static readonly Guid EmployeeId = Guid.Parse("2b91f0c4-1111-4222-8333-444455556666");

    /// <summary>America/Santo_Domingo es UTC−4 todo el año: 18:00Z se lee 14:00.</summary>
    private static readonly DateTime StartUtc = new(2026, 8, 20, 18, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task La_invitacion_sale_con_la_hora_en_la_zona_de_la_cita()
    {
        var harness = new Harness(preferencesEnabled: true);

        await Scheduled(harness, Invitation());

        Assert.Equal(2, harness.Gateway.Queued.Count);
        Assert.Contains("2026-08-20 14:00", harness.Render.LastVariables["start_local"]?.ToString());
        Assert.Equal("America/Santo_Domingo", harness.Render.LastVariables["time_zone"]);
    }

    /// <summary>
    /// El cliente invitado no tiene cuenta y por tanto no tiene preferencia. Callarlo junto con el
    /// empleado sería dejarlo sin saber de una cita a la que se espera que vaya.
    /// </summary>
    [Fact]
    public async Task Con_la_preferencia_apagada_calla_al_empleado_y_no_al_cliente_invitado()
    {
        var harness = new Harness(preferencesEnabled: false);

        await Scheduled(harness, Invitation());

        var email = Assert.Single(harness.Gateway.Queued);
        Assert.Equal("cliente@example.com", email.To);
        Assert.Equal(NotificationCategory.Calendar, harness.Preferences.LastAskedCategory);
        Assert.Equal(NotificationChannel.Email, harness.Preferences.LastAskedChannel);
    }

    /// <summary>
    /// El aviso de la cita movida lleva la hora vieja además de la nueva: quien tiene ocho citas esa
    /// semana no recuerda cuál era la que cambió.
    /// </summary>
    [Fact]
    public async Task El_aviso_de_movida_lleva_la_hora_vieja_y_la_nueva()
    {
        var harness = new Harness(preferencesEnabled: true);

        await AppointmentRescheduledConsumer.Handle(
            new AppointmentRescheduledIntegrationEvent
            {
                TenantId = TenantId,
                CorrelationId = "test",
                AppointmentId = Guid.NewGuid(),
                Scope = "Occurrence",
                PreviousStartUtc = StartUtc,
                NewStartUtc = StartUtc.AddHours(2),
                NewEndUtc = StartUtc.AddHours(3),
                TimeZoneId = "America/Santo_Domingo",
                Recipients = [new AppointmentRecipient("cliente@example.com", null)],
            },
            harness.Gateway,
            harness.Render,
            Options.Create(new PortalOptions()),
            harness.Preferences,
            harness.Dispatcher,
            NullLogger<AppointmentRescheduledIntegrationEvent>.Instance,
            new NoOpCorrelationContext(),
            CancellationToken.None
        );

        Assert.Equal("2026-08-20 14:00", harness.Render.LastVariables["previous_local"]);
        Assert.Equal("2026-08-20 16:00", harness.Render.LastVariables["new_local"]);
    }

    /// <summary>Sin destinatarios no se renderiza: pedirle a Scribe un correo que no se manda es trabajo tirado.</summary>
    [Fact]
    public async Task Sin_destinatarios_no_se_renderiza_nada()
    {
        var harness = new Harness(preferencesEnabled: true);

        await Scheduled(harness, Invitation() with { Recipients = [] });

        Assert.Empty(harness.Gateway.Queued);
        Assert.Equal(0, harness.Render.Calls);
    }

    private static Task Scheduled(Harness harness, AppointmentScheduledIntegrationEvent evt) =>
        AppointmentScheduledConsumer.Handle(
            evt,
            harness.Gateway,
            harness.Render,
            Options.Create(new PortalOptions()),
            harness.Preferences,
            harness.Dispatcher,
            NullLogger<AppointmentScheduledIntegrationEvent>.Instance,
            new NoOpCorrelationContext(),
            CancellationToken.None
        );

    private static AppointmentScheduledIntegrationEvent Invitation() =>
        new()
        {
            TenantId = TenantId,
            CorrelationId = "test",
            AppointmentId = Guid.NewGuid(),
            Title = "Revisión de la 1040",
            OrganizerUserId = EmployeeId,
            StartUtc = StartUtc,
            EndUtc = StartUtc.AddHours(1),
            TimeZoneId = "America/Santo_Domingo",
            IsRecurring = false,
            IsVirtual = false,
            Recipients =
            [
                new AppointmentRecipient("empleado@example.com", EmployeeId),
                new AppointmentRecipient("cliente@example.com", null),
            ],
        };

    private sealed class Harness(bool preferencesEnabled)
    {
        internal RecordingGateway Gateway { get; } = new();
        internal RecordingRenderClient Render { get; } = new();
        internal SwitchablePreferences Preferences { get; } = new(preferencesEnabled);
        internal NotificationDispatcher Dispatcher { get; } =
            new(
                new NoOpSmsSender(),
                new NoOpPushSender(),
                new NoOpPushDeviceTokenRepository(),
                new NoOpNotificationLogRepository(),
                new SwitchablePreferences(preferencesEnabled),
                new CapturingIntegrationEventPublisher(),
                new NoOpUnitOfWork(),
                new NoOpNotificationDispatchMetrics(),
                NullLogger<NotificationDispatcher>.Instance
            );
    }

    private sealed class NoOpSmsSender : ISmsSender
    {
        public Task<Result> SendAsync(
            string phoneNumber,
            string text,
            CancellationToken ct,
            string? templateKey,
            string? idempotencyToken
        ) => Task.FromResult(Result.Success());
    }

    private sealed class NoOpPushSender : IPushSender
    {
        public Task<Result> SendAsync(PushMessage message, CancellationToken ct = default) =>
            Task.FromResult(Result.Success());
    }

    private sealed class NoOpPushDeviceTokenRepository : IPushDeviceTokenRepository
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

    private sealed class NoOpNotificationLogRepository : INotificationLogRepository
    {
        public Task AddAsync(NotificationLog log, CancellationToken ct = default) => Task.CompletedTask;

        public Task<(IReadOnlyList<NotificationLog> Items, int TotalCount)> GetPagedAsync(
            Guid tenantId,
            NotificationStatus? status,
            int page,
            int size,
            CancellationToken ct = default
        ) => Task.FromResult<(IReadOnlyList<NotificationLog>, int)>(([], 0));

        public Task<NotificationLog?> GetByRelatedEventIdAsync(
            Guid tenantId,
            Guid relatedEventId,
            string templateKey,
            string recipient,
            CancellationToken ct = default
        ) => Task.FromResult<NotificationLog?>(null);
    }

    private sealed class NoOpUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct = default) => Task.FromResult(0);
    }

    private sealed class RecordingGateway : IEmailDispatchGateway
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

    private sealed class RecordingRenderClient : IScribeRenderClient
    {
        public IReadOnlyDictionary<string, object?> LastVariables { get; private set; } =
            new Dictionary<string, object?>();

        public int Calls { get; private set; }

        public Task<Result<ScribeRenderedEmail>> RenderAsync(
            string eventKey,
            Guid tenantId,
            IReadOnlyDictionary<string, object?> variables,
            CancellationToken ct = default
        )
        {
            Calls++;
            LastVariables = variables;
            return Task.FromResult(Result.Success(new ScribeRenderedEmail(eventKey, "<p>html</p>", "text")));
        }
    }

    private sealed class SwitchablePreferences(bool enabled) : IUserNotificationPreferenceRepository
    {
        public NotificationCategory? LastAskedCategory { get; private set; }
        public NotificationChannel? LastAskedChannel { get; private set; }

        public Task<bool> IsEnabledAsync(
            Guid tenantId,
            Guid userId,
            NotificationCategory category,
            NotificationChannel channel,
            CancellationToken ct = default
        )
        {
            LastAskedCategory = category;
            LastAskedChannel = channel;
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
