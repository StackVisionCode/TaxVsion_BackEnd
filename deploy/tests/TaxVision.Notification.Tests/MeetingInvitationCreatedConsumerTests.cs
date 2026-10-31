using BuildingBlocks.Messaging.CommunicationIntegrationEvents;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Application.Consumers.Communication;
using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Domain.Preferences;
using TaxVision.Notification.Tests.TestDoubles;

namespace TaxVision.Notification.Tests;

/// <summary>
/// La invitación a un meeting (evento de Communication) manda correo real, no solo in-app. Lo que
/// importa verificar: que se renderice con el <c>join_link</c> ya armado por Communication (subdominio
/// correcto), que se encole al email del invitado, que un invitado sin email siga recibiendo el in-app
/// sin encolar correo y, desde F6, que la hora salga en la zona de la oficina, que viaje el
/// <c>invite.ics</c> cuando hay fecha, y que la preferencia calle al de cuenta pero no al externo.
/// </summary>
public sealed class MeetingInvitationCreatedConsumerTests
{
    private static readonly Guid TenantId = Guid.Parse("d4879234-7370-4b58-b49c-094bd7c04847");
    private static readonly Guid MeetingId = Guid.Parse("5c0a2d6e-9d7b-4c1f-8e2a-3b4c5d6e7f80");
    private const string JoinUrl = "https://manfer.taxproffice.com/portal/client/meetings/accept/abc?token=t";

    /// <summary>America/Santo_Domingo es UTC−4 todo el año: 18:00Z se lee 14:00.</summary>
    private static readonly DateTime StartUtc = new(2026, 11, 5, 18, 0, 0, DateTimeKind.Utc);

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

    [Fact]
    public async Task La_hora_sale_en_la_zona_de_la_oficina_con_los_datos_de_la_reunion()
    {
        var harness = new Harness();

        await Handle(harness, Invitation("cliente@example.com"));

        var variables = harness.Render.LastVariables;
        Assert.Equal("Revisión de la 1040", variables["meeting_title"]);
        Assert.Equal("2026-11-05 14:00", variables["start_local"]);
        Assert.Equal("America/Santo_Domingo", variables["time_zone"]);
        Assert.Equal(45, variables["duration_minutes"]);
        Assert.Equal("María Pérez", variables["host_name"]);
        Assert.Equal("ABC-123", variables["meeting_code"]);
        Assert.Equal(false, variables["in_progress"]);
    }

    /// <summary>Sin zona la hora no se inventa: sale en UTC y lo dice, que es lo que evita el correo ambiguo.</summary>
    [Fact]
    public async Task Sin_zona_la_hora_sale_en_utc()
    {
        var harness = new Harness();

        await Handle(harness, Invitation("cliente@example.com") with { TimeZone = null });

        Assert.Equal("2026-11-05 18:00 UTC", harness.Render.LastVariables["start_local"]);
        Assert.Equal("UTC", harness.Render.LastVariables["time_zone"]);
    }

    [Fact]
    public async Task Con_fecha_adjunta_el_ics_con_method_request_y_uid_estable()
    {
        var harness = new Harness();

        await Handle(harness, Invitation("cliente@example.com"));

        var email = Assert.Single(harness.Gateway.Queued);
        var attachment = Assert.Single(email.InlineAttachments!);
        Assert.Equal("invite.ics", attachment.FileName);
        Assert.StartsWith("text/calendar; method=REQUEST", attachment.ContentType);
        var ics = InlineAttachmentReader.Text(attachment);
        Assert.Contains("METHOD:REQUEST", ics);
        Assert.Contains($"UID:meeting:{MeetingId:D}@taxoffix.io", ics);
        Assert.Contains("SEQUENCE:0", ics);
        Assert.Contains("DTSTART:20261105T180000Z", ics);
        Assert.Contains("DTEND:20261105T184500Z", ics);
        Assert.Contains(
            "ATTENDEE;ROLE=REQ-PARTICIPANT;PARTSTAT=NEEDS-ACTION;RSVP=TRUE;CN=Cliente:mailto:cliente@example.com",
            ics.Replace("\r\n ", string.Empty)
        );
    }

    /// <summary>Una reunión en curso no tiene fecha: el correo dice "en curso" y no adjunta calendario.</summary>
    [Fact]
    public async Task En_curso_marca_in_progress_y_no_adjunta_ics()
    {
        var harness = new Harness();

        await Handle(
            harness,
            Invitation("cliente@example.com") with
            {
                MeetingStatus = "Live",
                ScheduledForUtc = null,
            }
        );

        var email = Assert.Single(harness.Gateway.Queued);
        Assert.Null(email.InlineAttachments);
        Assert.Equal(true, harness.Render.LastVariables["in_progress"]);
        Assert.Null(harness.Render.LastVariables["start_local"]);
    }

    /// <summary>Con la preferencia de Calendar apagada el de cuenta no recibe correo; el externo sí.</summary>
    [Fact]
    public async Task Con_la_preferencia_apagada_calla_al_de_cuenta_y_no_al_externo()
    {
        var harness = new Harness(preferencesEnabled: false);

        await Handle(harness, Invitation("cliente@example.com"));
        Assert.Empty(harness.Gateway.Queued);
        Assert.Equal(NotificationCategory.Calendar, harness.Preferences.LastAskedCategory);
        Assert.Equal(NotificationChannel.Email, harness.Preferences.LastAskedChannel);

        await Handle(
            harness,
            Invitation("externo@example.com") with
            {
                InviteeKind = "External",
                InviteeUserId = null,
            }
        );
        var email = Assert.Single(harness.Gateway.Queued);
        Assert.Equal("externo@example.com", email.To);
    }

    private static Task Handle(Harness harness, MeetingInvitationCreatedIntegrationEvent evt) =>
        MeetingInvitationCreatedConsumer.Handle(
            evt,
            harness.Dispatcher.Dispatcher,
            harness.Gateway,
            harness.Render,
            harness.Preferences,
            new CalendarInviteBuilder(TestPlatform.Settings),
            TestPlatform.Settings,
            Options.Create(new PortalOptions()),
            new TestCorrelationContext(),
            NullLogger<MeetingInvitationCreatedLog>.Instance,
            CancellationToken.None
        );

    private static MeetingInvitationCreatedIntegrationEvent Invitation(string? email) =>
        new()
        {
            TenantId = TenantId,
            CorrelationId = "test",
            InvitationId = Guid.NewGuid(),
            MeetingId = MeetingId,
            InviteeKind = "Customer",
            InviteeUserId = Guid.NewGuid(),
            InviteeEmail = email,
            InviteeName = "Cliente",
            TokenHash = "hash",
            ExpiresAtUtc = new DateTime(2026, 11, 6, 12, 0, 0, DateTimeKind.Utc),
            JoinUrl = JoinUrl,
            MeetingTitle = "Revisión de la 1040",
            MeetingStatus = "Scheduled",
            ShortCode = "ABC-123",
            ScheduledForUtc = StartUtc,
            DurationMinutes = 45,
            HostUserId = Guid.NewGuid(),
            HostDisplayName = "María Pérez",
            TimeZone = "America/Santo_Domingo",
            InvitedByUserId = Guid.NewGuid(),
            InvitedByDisplayName = "María Pérez",
        };

    private sealed class Harness
    {
        internal RecordingNotificationLogs Logs { get; } = new();
        internal RecordingEmailGateway Gateway { get; } = new();
        internal RecordingScribeRenderClient Render { get; } = new();
        internal SwitchableNotificationPreferences Preferences { get; }
        internal NotificationDispatcherHarness Dispatcher { get; }

        internal Harness(bool preferencesEnabled = true)
        {
            Preferences = new SwitchableNotificationPreferences(preferencesEnabled);
            // El in-app no depende de la preferencia de email que se prueba acá: el dispatcher usa la suya (siempre on).
            Dispatcher = new NotificationDispatcherHarness(logs: Logs);
        }
    }
}
