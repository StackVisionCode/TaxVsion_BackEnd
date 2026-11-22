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
        // Scribe imprime la hora en la zona de la cita, con su rótulo (ya no "2026-08-20 14:00 America/...").
        Assert.Equal("2026-08-20T18:00:00.0000000Z", TypedDates.Instant(harness.Render.LastVariables["start_local"]));
        Assert.Equal("America/Santo_Domingo", harness.Render.LastReader?.TimeZoneId);
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
            harness.Render,
            Options.Create(new PortalOptions()),
            new FakeTenantHostResolver(),
            harness.Dispatcher,
            FixedRecipientLocale.English,
            new NoOpCorrelationContext(),
            CancellationToken.None
        );

        Assert.Equal(
            "2026-08-20T18:00:00.0000000Z",
            TypedDates.Instant(harness.Render.LastVariables["previous_local"])
        );
        Assert.Equal("2026-08-20T20:00:00.0000000Z", TypedDates.Instant(harness.Render.LastVariables["new_local"]));
        Assert.Equal("America/Santo_Domingo", harness.Render.LastReader?.TimeZoneId);
    }

    /// <summary>
    /// El empleado ve el logo del CRM y el cliente invitado el del portal: un render por audiencia, y
    /// cada destinatario recibe el suyo.
    /// </summary>
    [Fact]
    public async Task El_empleado_recibe_el_render_del_equipo_y_el_cliente_el_del_cliente()
    {
        var harness = new Harness(preferencesEnabled: true);

        await Scheduled(harness, Invitation());

        Assert.Equal([ScribeRenderAudience.Staff, ScribeRenderAudience.Client], harness.Render.Audiences);
        var employee = Assert.Single(harness.Gateway.Queued, e => e.To == "empleado@example.com");
        var client = Assert.Single(harness.Gateway.Queued, e => e.To == "cliente@example.com");
        Assert.EndsWith(":Staff", employee.Subject);
        Assert.EndsWith(":Client", client.Subject);
    }

    /// <summary>Sin destinatarios no se renderiza: pedirle a Scribe un correo que no se manda es trabajo tirado.</summary>
    /// <summary>
    /// Cada invitado responde con su propio enlace (el token va en el fragmento: no llega al servidor
    /// ni a los logs). El organizador ya aceptó: sin botones.
    /// </summary>
    [Fact]
    public async Task Each_invitee_gets_their_own_rsvp_buttons_and_the_organizer_none()
    {
        var harness = new Harness(preferencesEnabled: true);
        var evt = Invitation() with
        {
            Recipients =
            [
                new AppointmentRecipient("empleado@example.com", EmployeeId, RsvpToken: "tok-organizer"),
                new AppointmentRecipient("cliente@example.com", null, RsvpToken: "tok-client"),
            ],
        };

        await Scheduled(harness, evt);

        var withButtons = harness.Render.AllVariables.Where(v => v.ContainsKey("rsvp_accept_link")).ToList();
        var client = Assert.Single(withButtons);
        Assert.Equal("https://manfer.taxoffix.io/rsvp#token=tok-client&answer=accepted", client["rsvp_accept_link"]);
        Assert.Equal("https://manfer.taxoffix.io/rsvp#token=tok-client&answer=tentative", client["rsvp_maybe_link"]);
        Assert.Equal("https://manfer.taxoffix.io/rsvp#token=tok-client&answer=declined", client["rsvp_decline_link"]);
        Assert.Equal(2, harness.Gateway.Queued.Count);
    }

    /// <summary>Cambió el nombre o el lugar: avisa a todos menos a quien editó, con el nombre viejo al lado.</summary>
    [Fact]
    public async Task Edited_details_reach_every_attendee_but_the_editor()
    {
        var harness = new Harness(preferencesEnabled: true);

        await AppointmentDetailsChangedConsumer.Handle(
            new AppointmentDetailsChangedIntegrationEvent
            {
                TenantId = TenantId,
                CorrelationId = "test",
                AppointmentId = Guid.NewGuid(),
                Title = "Revisión final",
                PreviousTitle = "Consulta inicial",
                Location = "Office 2",
                ChangedFields = ["Title", "Location"],
                StartUtc = StartUtc,
                TimeZoneId = "America/Santo_Domingo",
                OrganizerUserId = EmployeeId,
                ChangedByUserId = EmployeeId,
                MeetingId = Guid.NewGuid(),
                Recipients =
                [
                    new AppointmentRecipient("empleado@example.com", EmployeeId),
                    new AppointmentRecipient("cliente@example.com", null),
                ],
            },
            harness.Render,
            Options.Create(new PortalOptions()),
            harness.Dispatcher,
            FixedRecipientLocale.English,
            new NoOpCorrelationContext(),
            CancellationToken.None
        );

        Assert.Equal(["cliente@example.com"], harness.Gateway.Queued.Select(e => e.To));
        Assert.Equal("Consulta inicial", harness.Render.LastVariables["previous_title"]);
        Assert.Equal("Office 2", harness.Render.LastVariables["location"]);
        Assert.Equal(true, harness.Render.LastVariables["title_changed"]);
        Assert.Equal(false, harness.Render.LastVariables["description_changed"]);
    }

    /// <summary>Si la reservó el cliente en el portal, el aviso al preparador lo dice, con su nombre.</summary>
    [Fact]
    public void A_booking_from_the_portal_is_announced_as_booked_online_by_the_client()
    {
        var booked = AppointmentScheduledConsumer.Announcement(
            Invitation() with
            {
                BookedByCustomerName = "Ana Client",
            }
        );
        var internalOne = AppointmentScheduledConsumer.Announcement(Invitation());

        Assert.Equal(
            ("calendar.appointment_booked_online", "Booked online by Ana Client: Revisión de la 1040"),
            booked
        );
        Assert.Equal(("calendar.appointment_scheduled", "New appointment: Revisión de la 1040"), internalOne);
    }

    /// <summary>La solicitud del portal llega a la oficina con el nombre del cliente, para aprobarla o asignarla.</summary>
    [Fact]
    public void A_booking_request_is_announced_to_the_office_with_the_clients_name()
    {
        var heading = AppointmentBookingRequestedConsumer.Heading(
            new AppointmentBookingRequestedIntegrationEvent
            {
                TenantId = TenantId,
                CorrelationId = "test",
                AppointmentId = Guid.NewGuid(),
                Title = "Consulta inicial",
                TimeZoneId = "America/Santo_Domingo",
                OrganizerUserId = EmployeeId,
                CustomerName = "Minato Kenyimura",
            }
        );

        Assert.Equal("Booking request from Minato Kenyimura: Consulta inicial", heading);
    }

    /// <summary>Rechazada: el cliente recibe un correo con el motivo y la hora que pidió.</summary>
    [Fact]
    public async Task A_declined_request_emails_the_client_with_the_reason()
    {
        var harness = new Harness(preferencesEnabled: true);

        await AppointmentBookingDeclinedConsumer.Handle(
            new AppointmentBookingDeclinedIntegrationEvent
            {
                TenantId = TenantId,
                CorrelationId = "test",
                AppointmentId = Guid.NewGuid(),
                Title = "Consulta inicial",
                StartUtc = StartUtc,
                TimeZoneId = "America/Santo_Domingo",
                Reason = "We're closed that day",
                Recipients = [new AppointmentRecipient("minato@client.test", null, Guid.NewGuid(), "Minato")],
            },
            harness.Render,
            Options.Create(new PortalOptions()),
            new FakeTenantHostResolver("manfer.taxoffix.io"),
            harness.Dispatcher,
            NoPortalRecipientResolver.Instance,
            FixedRecipientLocale.English,
            new NoOpCorrelationContext(),
            CancellationToken.None
        );

        Assert.Equal(["minato@client.test"], harness.Gateway.Queued.Select(e => e.To));
        Assert.Equal("We're closed that day", harness.Render.LastVariables["reason"]);
        // "Book another time" lleva a reservar en el portal de la oficina, no a una URL fija.
        Assert.EndsWith(
            "manfer.taxoffix.io/portal/client/calendar/book",
            harness.Render.LastVariables["portal_link"]?.ToString()
        );
        Assert.Equal("2026-08-20T18:00:00.0000000Z", TypedDates.Instant(harness.Render.LastVariables["start_local"]));
    }

    [Fact]
    public async Task Sin_destinatarios_no_se_renderiza_nada()
    {
        var harness = new Harness(preferencesEnabled: true);

        await Scheduled(harness, Invitation() with { Recipients = [] });

        Assert.Empty(harness.Gateway.Queued);
        Assert.Equal(0, harness.Render.Calls);
    }

    /// <summary>NOT-03: un empleado inactivo no recibe el correo de la cita; el cliente invitado sí.</summary>
    [Fact]
    public async Task Inactive_attendee_gets_no_email()
    {
        var harness = new Harness(preferencesEnabled: true);
        harness.Directory.Seed(TenantId, EmployeeId, "empleado@example.com", isActive: false);

        await Scheduled(harness, Invitation());

        var email = Assert.Single(harness.Gateway.Queued);
        Assert.Equal("cliente@example.com", email.To);
    }

    /// <summary>
    /// Cita virtual: los invitados reciben la invitación de la sala (Communication, con su enlace). El
    /// correo de la cita queda para el organizador, que es el host y no tiene invitación: uno por persona.
    /// </summary>
    [Fact]
    public async Task Virtual_appointment_emails_only_the_organizer_the_room_invites_the_rest()
    {
        var harness = new Harness(preferencesEnabled: true);
        var evt = Invitation() with
        {
            IsVirtual = true,
            Recipients =
            [
                new AppointmentRecipient("organizer@example.com", EmployeeId),
                new AppointmentRecipient("staff@example.com", Guid.NewGuid()),
                new AppointmentRecipient("cliente@example.com", null),
            ],
        };

        await Scheduled(harness, evt);

        Assert.Equal(["organizer@example.com"], harness.Gateway.Queued.Select(e => e.To));
    }

    [Fact]
    public async Task Someone_added_to_a_virtual_appointment_is_left_to_the_room_invitation()
    {
        var harness = new Harness(preferencesEnabled: true);

        await AppointmentAttendeeAddedConsumer.Handle(
            new AppointmentAttendeeAddedIntegrationEvent
            {
                TenantId = TenantId,
                CorrelationId = "test",
                AppointmentId = Guid.NewGuid(),
                AttendeeKind = "InternalUser",
                UserId = Guid.NewGuid(),
                Email = "andres@example.com",
                Title = "Consulta inicial",
                StartUtc = StartUtc,
                TimeZoneId = "America/Santo_Domingo",
                IsRecurring = false,
                IsVirtual = true,
            },
            harness.Render,
            Options.Create(new PortalOptions()),
            new FakeTenantHostResolver(),
            harness.Dispatcher,
            FixedRecipientLocale.English,
            new NoOpCorrelationContext(),
            CancellationToken.None
        );

        Assert.Empty(harness.Gateway.Queued);
    }

    [Fact]
    public async Task Cancelling_a_virtual_appointment_with_a_room_emails_only_the_organizer()
    {
        var harness = new Harness(preferencesEnabled: true);

        await AppointmentCancelledConsumer.Handle(
            new AppointmentCancelledIntegrationEvent
            {
                TenantId = TenantId,
                CorrelationId = "test",
                AppointmentId = Guid.NewGuid(),
                Scope = "EntireSeries",
                MeetingId = Guid.NewGuid(),
                OrganizerUserId = EmployeeId,
                Recipients =
                [
                    new AppointmentRecipient("organizer@example.com", EmployeeId),
                    new AppointmentRecipient("cliente@example.com", null),
                ],
            },
            harness.Render,
            Options.Create(new PortalOptions()),
            harness.Dispatcher,
            FixedRecipientLocale.English,
            new NoOpCorrelationContext(),
            CancellationToken.None
        );

        Assert.Equal(["organizer@example.com"], harness.Gateway.Queued.Select(e => e.To));
    }

    /// <summary>Al cancelar una ocurrencia de una serie semanal, el correo dice cuál fue.</summary>
    [Fact]
    public async Task The_cancellation_email_says_which_appointment_was_cancelled()
    {
        var harness = new Harness(preferencesEnabled: true);

        await AppointmentCancelledConsumer.Handle(
            new AppointmentCancelledIntegrationEvent
            {
                TenantId = TenantId,
                CorrelationId = "test",
                AppointmentId = Guid.NewGuid(),
                Scope = "ThisOccurrence",
                OriginalStartUtc = StartUtc,
                StartUtc = StartUtc,
                TimeZoneId = "America/Santo_Domingo",
                Recipients = [new AppointmentRecipient("cliente@example.com", null)],
            },
            harness.Render,
            Options.Create(new PortalOptions()),
            harness.Dispatcher,
            FixedRecipientLocale.English,
            new NoOpCorrelationContext(),
            CancellationToken.None
        );

        Assert.Equal("2026-08-20T18:00:00.0000000Z", TypedDates.Instant(harness.Render.LastVariables["start_local"]));
        Assert.Equal("America/Santo_Domingo", harness.Render.LastReader?.TimeZoneId);
    }

    /// <summary>
    /// Un render por idioma y audiencia, no por persona: dos compañeros en español comparten el suyo. La hora va
    /// en la zona de la cita, no en la de cada lector.
    /// </summary>
    [Fact]
    public async Task One_render_per_language_and_audience()
    {
        var harness = new Harness(preferencesEnabled: true);
        var spanishA = Guid.NewGuid();
        var spanishB = Guid.NewGuid();
        var english = Guid.NewGuid();
        var locales = new FixedRecipientLocale("en", "America/Chicago");
        locales.ByUser[spanishA] = "es";
        locales.ByUser[spanishB] = "es";

        await Scheduled(
            harness,
            Invitation() with
            {
                Recipients =
                [
                    new AppointmentRecipient("a@example.com", spanishA),
                    new AppointmentRecipient("b@example.com", spanishB),
                    new AppointmentRecipient("c@example.com", english),
                    new AppointmentRecipient("cliente@example.com", null),
                ],
            },
            locales
        );

        Assert.Equal(4, harness.Gateway.Queued.Count);
        Assert.Equal(
            ["Client:En", "Staff:En", "Staff:Es"],
            harness
                .Render.Audiences.Zip(harness.Render.AllVariables, (audience, v) => $"{audience}:{v["language"]}")
                .Order()
        );
        Assert.All(harness.Render.Readers, reader => Assert.Equal("America/Santo_Domingo", reader.TimeZoneId));
        Assert.Equal(
            harness.Gateway.Queued.Single(e => e.To == "a@example.com").Subject,
            harness.Gateway.Queued.Single(e => e.To == "b@example.com").Subject
        );
    }

    private static Task Scheduled(
        Harness harness,
        AppointmentScheduledIntegrationEvent evt,
        IRecipientLocale? locales = null
    ) =>
        AppointmentScheduledConsumer.Handle(
            evt,
            harness.Render,
            Options.Create(new PortalOptions()),
            new FakeTenantHostResolver("manfer.taxoffix.io"),
            harness.Dispatcher,
            locales ?? FixedRecipientLocale.English,
            NoPortalRecipientResolver.Instance,
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

    private sealed class Harness
    {
        internal Harness(bool preferencesEnabled)
        {
            Preferences = new SwitchablePreferences(preferencesEnabled);
            Gateway = new GatedEmailGateway(Preferences, Directory);
            Dispatcher = TestDispatchers.Create(
                new NoOpSmsSender(),
                new NoOpPushSender(),
                new NoOpPushDeviceTokenRepository(),
                new NoOpNotificationLogRepository(),
                // La preferencia la decide ya el dispatcher: misma instancia que el gateway.
                TestDeliveryGates.For(Preferences, Directory),
                new CapturingIntegrationEventPublisher(),
                new NoOpUnitOfWork(),
                new NoOpNotificationDispatchMetrics(),
                new TaxVision.Notification.Tests.TestDoubles.InlinePushAttemptRecorder(),
                Gateway,
                Render
            );
        }

        // El gateway real aplica el gate de entrega (inactivo, preferencia, catálogo).
        internal InMemoryUserEmailDirectoryRepository Directory { get; } = new();
        internal GatedEmailGateway Gateway { get; }
        internal RecordingRenderClient Render { get; } = new();
        internal SwitchablePreferences Preferences { get; }
        internal NotificationDispatcher Dispatcher { get; }
    }

    private sealed class NoOpSmsSender : ISmsSender
    {
        public Task<Result> SendAsync(
            string phoneNumber,
            string text,
            CancellationToken ct,
            string? templateKey,
            string? idempotencyToken,
            string? sourceRef = null
        ) => Task.FromResult(Result.Success());
    }

    private sealed class NoOpPushSender : IPushSender
    {
        public Task<Result> SendAsync(PushMessage message, CancellationToken ct = default) =>
            Task.FromResult(Result.Success());
    }

    private sealed class NoOpPushDeviceTokenRepository : IPushDeviceTokenRepository
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
        public Task<NotificationLog?> GetByRelatedEventAsync(
            Guid tenantId,
            Guid relatedEventId,
            string kind,
            NotificationChannel channel,
            string recipient,
            CancellationToken ct = default
        ) => Task.FromResult<NotificationLog?>(null);

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

    private sealed class RecordingRenderClient : IScribeRenderClient, IScribeReaderRenderClient
    {
        public ScribeReader? LastReader { get; private set; }

        public Task<Result<ScribeRenderedEmail>> RenderForReaderAsync(
            string eventKey,
            Guid tenantId,
            IReadOnlyDictionary<string, object?> variables,
            ScribeRenderAudience audience,
            ScribeReader reader,
            CancellationToken ct = default
        )
        {
            LastReader = reader;
            Readers.Add(reader);
            return RenderAsync(eventKey, tenantId, variables, audience, ct);
        }

        public List<ScribeReader> Readers { get; } = [];

        public Task<Result<ScribeRenderedText>> RenderTextAsync(
            string eventKey,
            Guid tenantId,
            IReadOnlyDictionary<string, object?> variables,
            CancellationToken ct = default
        ) => TestDoubles.ScribeTestDefaults.TextUnsupported();

        public IReadOnlyDictionary<string, object?> LastVariables { get; private set; } =
            new Dictionary<string, object?>();

        public int Calls { get; private set; }

        public List<ScribeRenderAudience> Audiences { get; } = [];

        public Task<Result<ScribeRenderedEmail>> RenderAsync(
            string eventKey,
            Guid tenantId,
            IReadOnlyDictionary<string, object?> variables,
            CancellationToken ct = default
        ) => RenderAsync(eventKey, tenantId, variables, ScribeRenderAudience.Staff, ct);

        // El asunto lleva la audiencia para saber qué render recibió cada destinatario.
        public List<IReadOnlyDictionary<string, object?>> AllVariables { get; } = [];

        public Task<Result<ScribeRenderedEmail>> RenderAsync(
            string eventKey,
            Guid tenantId,
            IReadOnlyDictionary<string, object?> variables,
            ScribeRenderAudience audience,
            CancellationToken ct = default
        )
        {
            Calls++;
            LastVariables = variables;
            AllVariables.Add(variables);
            Audiences.Add(audience);
            return Task.FromResult(
                Result.Success(new ScribeRenderedEmail($"{eventKey}:{audience}", "<p>html</p>", "text"))
            );
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
