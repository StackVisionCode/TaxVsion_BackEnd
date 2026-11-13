using System.Security.Cryptography;
using System.Text;
using BuildingBlocks.Common;
using BuildingBlocks.Infrastructure.Security;
using BuildingBlocks.Messaging.CalendarIntegrationEvents;
using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using BuildingBlocks.Results;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Application.Consumers.Calendar.PublicBookings;
using TaxVision.Notification.Domain.Notifications;
using Xunit;

namespace TaxVision.Notification.Tests;

/// <summary>
/// Los correos de la demo pública del landing. Lo que se cuida (guía §7.4–7.5): el código y el enlace se
/// descifran solo con su atadura y salen confidenciales; cada correo tiene una clave estable (reintentos y
/// reentregas no duplican); un aviso que llega después de la cancelación no sale; y el .ics lleva el mismo UID
/// con el SEQUENCE del cambio.
/// </summary>
public sealed class PublicBookingEmailsTests
{
    private static readonly Guid Tenant = Guid.Parse("8f58a521-4c25-4d91-9f4e-7ad5df14c001");
    private static readonly Guid BookingId = Guid.Parse("0f1e2d3c-4b5a-6978-8796-a5b4c3d2e1f0");
    private static readonly DateTime Slot = new(2026, 11, 18, 15, 0, 0, DateTimeKind.Utc);
    private const string Token = "management-token-value";

    private readonly JsonProtectedPayloadCodec _codec = new(
        new AesGcmRotatingSecretProtector(
            Options.Create(
                new RotatingSecretProtectionOptions
                {
                    MasterKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
                    MasterKeyVersion = 1,
                }
            )
        )
    );
    private readonly RecordingScribe _scribe = new();
    private readonly MemoryLogs _logs = new();
    private readonly RecordingGateway _gateway;

    public PublicBookingEmailsTests() => _gateway = new RecordingGateway(_logs);

    // ---------- Código ----------

    [Fact]
    public async Task The_code_goes_only_to_the_visitor_confidential_and_decrypted()
    {
        await PublicBookingCodeRequestedConsumer.Handle(CodeEvent("123456"), Emails(), default);

        var email = Assert.Single(_gateway.Queued);
        Assert.Equal("jane@acme-tax.com", email.To);
        Assert.True(email.Confidential);
        Assert.Equal("demo.verification_code.v1", email.TemplateKey);
        Assert.Equal("123456", _scribe.Variables("demo.verification_code.v1")["code"]);
        Assert.Equal(PublicBookingEmails.KeyFor(BookingId, "code:1"), email.RelatedEventId);
    }

    [Fact]
    public async Task A_code_that_does_not_decrypt_sends_nothing_and_does_not_retry()
    {
        var evt = CodeEvent("123456") with { Send = 2 };

        await PublicBookingCodeRequestedConsumer.Handle(evt, Emails(), default);

        Assert.Empty(_gateway.Queued);
    }

    // ---------- Confirmación ----------

    [Fact]
    public async Task The_confirmation_gives_each_side_its_own_email_and_the_same_calendar_entry()
    {
        await PublicBookingConfirmedConsumer.Handle(Confirmed(), Emails(), default);

        Assert.Equal(2, _gateway.Queued.Count);
        var visitor = _gateway.Queued.Single(e => e.To == "jane@acme-tax.com");
        var host = _gateway.Queued.Single(e => e.To == "demos@taxoffix.io");

        Assert.True(visitor.Confidential);
        Assert.Equal("demo.confirmed.v1", visitor.TemplateKey);
        var forVisitor = _scribe.Variables("demo.confirmed.v1");
        Assert.Equal($"https://www.taxoffix.io/demo/manage#token={Token}", forVisitor["manage_url"]);
        Assert.Equal("Wednesday, November 18, 2026", forVisitor["date_local"]);
        Assert.Equal("10:00 AM", forVisitor["time_local"]);
        Assert.Equal("New York time, GMT-5", forVisitor["time_zone_label"]);
        Assert.Equal("https://meet.google.com/abc-defg-hij", forVisitor["meeting_url"]);
        Assert.StartsWith(
            "https://calendar.google.com/calendar/render?action=TEMPLATE",
            (string)forVisitor["google_calendar_url"]!
        );
        Assert.StartsWith(
            "https://outlook.live.com/calendar/0/deeplink/compose?",
            (string)forVisitor["outlook_calendar_url"]!
        );

        Assert.False(host.Confidential);
        Assert.Equal("demo.new_booking.v1", host.TemplateKey);
        var forHost = _scribe.Variables("demo.new_booking.v1");
        Assert.Equal("7:00 AM", forHost["time_local"]);
        Assert.Equal("Los Angeles time, GMT-8", forHost["time_zone_label"]);
        Assert.Equal("Acme Tax", forHost["company"]);
        Assert.Equal("WhatsApp", forHost["heard_about"]);
        Assert.Equal("Hero", forHost["entry_source"]);
        Assert.False(forHost.ContainsKey("manage_url"));

        foreach (var email in _gateway.Queued)
        {
            var ics = Ics(email);
            Assert.Contains("METHOD:PUBLISH\r\n", ics);
            Assert.Contains($"UID:public-booking-{BookingId:N}@taxoffix.io\r\n", ics);
            Assert.Contains("SEQUENCE:0\r\n", ics);
            Assert.Contains("LOCATION:https://meet.google.com/abc-defg-hij\r\n", ics);
            Assert.Contains("TRIGGER:-PT1440M\r\n", ics);
        }
    }

    [Fact]
    public async Task A_visitor_in_spanish_reads_the_date_in_spanish()
    {
        var evt = Confirmed() with { Language = "es" };

        await PublicBookingConfirmedConsumer.Handle(evt, Emails(), default);

        var forVisitor = _scribe.Variables("demo.confirmed.v1");
        Assert.Equal("Es", forVisitor["language"]);
        Assert.Equal("miércoles 18 de noviembre de 2026", forVisitor["date_local"]);
        Assert.Equal("10:00", forVisitor["time_local"]);
        Assert.Equal("hora de New York, GMT-5", forVisitor["time_zone_label"]);
    }

    /// <summary>
    /// La ciudad sola confunde a quien vive en otro país de la misma zona (Windows "SA Western" sale como La Paz):
    /// el desfase del día de la demo, con horario de verano y medias horas, dice la hora sin ambigüedad.
    /// </summary>
    [Theory]
    [InlineData("America/La_Paz", "La Paz time, GMT-4")]
    [InlineData("Asia/Kolkata", "Kolkata time, GMT+5:30")]
    [InlineData("Europe/London", "London time, GMT")]
    public async Task The_zone_label_carries_the_offset_of_the_demo_day(string zone, string label)
    {
        var evt = Confirmed() with { Visitor = Visitor with { TimeZoneId = zone } };

        await PublicBookingConfirmedConsumer.Handle(evt, Emails(), default);

        Assert.Equal(label, _scribe.Variables("demo.confirmed.v1")["time_zone_label"]);
    }

    [Fact]
    public async Task The_same_confirmation_delivered_twice_keeps_the_same_keys()
    {
        await PublicBookingConfirmedConsumer.Handle(Confirmed(), Emails(), default);
        await PublicBookingConfirmedConsumer.Handle(Confirmed() with { EventId = Guid.NewGuid() }, Emails(), default);

        Assert.Equal(2, _gateway.Queued.Count);
    }

    [Fact]
    public async Task A_confirmation_that_arrives_after_the_cancellation_is_not_sent()
    {
        await PublicBookingCancelledConsumer.Handle(Cancelled(), Emails(), default);
        _gateway.Queued.Clear();

        await PublicBookingConfirmedConsumer.Handle(Confirmed(), Emails(), default);
        await PublicBookingRescheduledConsumer.Handle(Rescheduled(), Emails(), default);

        Assert.Empty(_gateway.Queued);
    }

    /// <summary>
    /// La cola procesa en paralelo (guía §7.5): un aviso viejo que llega después del cambio siguiente mandaría la
    /// hora anterior. Cada cambio sube el SEQUENCE en uno, así que si ya salió el siguiente, el viejo no sale.
    /// </summary>
    [Fact]
    public async Task A_notice_older_than_the_next_move_is_not_sent()
    {
        await PublicBookingRescheduledConsumer.Handle(Rescheduled(), Emails(), default);
        _gateway.Queued.Clear();

        await PublicBookingConfirmedConsumer.Handle(Confirmed() with { IcsSequence = 1 }, Emails(), default);
        await PublicBookingRescheduledConsumer.Handle(
            Rescheduled() with
            {
                IcsSequence = 1,
                ManagementTokenVersion = 2,
                ProtectedManagementToken = ProtectedToken(2),
            },
            Emails(),
            default
        );

        Assert.Empty(_gateway.Queued);
    }

    // ---------- Cambio, cancelación y recordatorio ----------

    [Fact]
    public async Task A_move_tells_both_sides_what_changed_and_updates_the_calendar_entry()
    {
        await PublicBookingRescheduledConsumer.Handle(Rescheduled(), Emails(), default);

        var visitor = _gateway.Queued.Single(e => e.To == "jane@acme-tax.com");
        var host = _gateway.Queued.Single(e => e.To == "demos@taxoffix.io");
        Assert.True(visitor.Confidential);
        Assert.Equal("demo.rescheduled.v1", visitor.TemplateKey);
        var forVisitor = _scribe.Variables("demo.rescheduled.v1", "jane@acme-tax.com");
        Assert.Equal(false, forVisitor["is_host"]);
        Assert.Equal("Staff", forVisitor["changed_by"]);
        Assert.Equal("Host conflict", forVisitor["reason"]);
        Assert.Equal("Tuesday, November 17, 2026", forVisitor["previous_date_local"]);
        Assert.Equal($"https://www.taxoffix.io/demo/manage#token={Token}", forVisitor["manage_url"]);
        Assert.Equal(true, _scribe.Variables("demo.rescheduled.v1", "demos@taxoffix.io")["is_host"]);
        Assert.Contains("SEQUENCE:2\r\n", Ics(visitor));
        Assert.Contains("SEQUENCE:2\r\n", Ics(host));
        Assert.NotEqual(visitor.RelatedEventId, host.RelatedEventId);
    }

    [Fact]
    public async Task A_cancellation_removes_the_entry_from_both_calendars()
    {
        await PublicBookingCancelledConsumer.Handle(Cancelled(), Emails(), default);

        Assert.Equal(2, _gateway.Queued.Count);
        var forVisitor = _scribe.Variables("demo.cancelled.v1", "jane@acme-tax.com");
        Assert.Equal("https://www.taxoffix.io/demo", forVisitor["book_again_url"]);
        foreach (var email in _gateway.Queued)
        {
            Assert.Contains("METHOD:CANCEL\r\n", Ics(email));
            Assert.Contains("SEQUENCE:3\r\n", Ics(email));
            Assert.DoesNotContain("VALARM", Ics(email));
        }
    }

    [Fact]
    public async Task The_reminder_goes_to_the_visitor_with_the_meeting_and_a_fresh_link()
    {
        await PublicBookingReminderDueConsumer.Handle(Reminder(), Emails(), default);

        var email = Assert.Single(_gateway.Queued);
        Assert.Equal("jane@acme-tax.com", email.To);
        Assert.True(email.Confidential);
        var variables = _scribe.Variables("demo.reminder.v1");
        Assert.Equal("https://meet.google.com/abc-defg-hij", variables["meeting_url"]);
        Assert.Equal($"https://www.taxoffix.io/demo/manage#token={Token}", variables["manage_url"]);
        Assert.Equal(PublicBookingEmails.KeyFor(BookingId, "reminder:v2"), email.RelatedEventId);
    }

    /// <summary>
    /// Ya tenía una demo y pidió otra: al dueño del email le llega su demo con el enlace nuevo, confidencial y en
    /// su idioma. La clave es por versión del enlace: el mismo aviso no sale dos veces.
    /// </summary>
    [Fact]
    public async Task Asking_again_with_a_booked_demo_mails_the_demo_and_a_fresh_link()
    {
        await PublicBookingAlreadyBookedConsumer.Handle(AlreadyBooked(), Emails(), default);
        await PublicBookingAlreadyBookedConsumer.Handle(
            AlreadyBooked() with
            {
                EventId = Guid.NewGuid(),
            },
            Emails(),
            default
        );

        var email = Assert.Single(_gateway.Queued);
        Assert.Equal("jane@acme-tax.com", email.To);
        Assert.True(email.Confidential);
        Assert.Equal("demo.already_booked.v1", email.TemplateKey);
        Assert.Equal(PublicBookingEmails.KeyFor(BookingId, "already:v4"), email.RelatedEventId);
        var variables = _scribe.Variables("demo.already_booked.v1");
        Assert.Equal("Es", variables["language"]);
        Assert.Equal("miércoles 18 de noviembre de 2026", variables["date_local"]);
        Assert.Equal($"https://www.taxoffix.io/demo/manage#token={Token}", variables["manage_url"]);
        Assert.Equal("https://meet.google.com/abc-defg-hij", variables["meeting_url"]);
    }

    [Fact]
    public async Task No_already_booked_notice_after_the_cancellation()
    {
        await PublicBookingCancelledConsumer.Handle(Cancelled(), Emails(), default);
        _gateway.Queued.Clear();

        await PublicBookingAlreadyBookedConsumer.Handle(AlreadyBooked(), Emails(), default);

        Assert.Empty(_gateway.Queued);
    }

    // ---------- Ayudas ----------

    private PublicBookingEmails Emails() =>
        new(
            _scribe,
            _gateway,
            new CalendarInviteBuilder(TestPlatform.Settings),
            _codec,
            TestPlatform.Settings,
            _logs,
            new Correlation(),
            NullLogger<PublicBookingEmails>.Instance
        );

    private PublicBookingCodeRequestedIntegrationEvent CodeEvent(string code) =>
        new()
        {
            TenantId = Tenant,
            BookingId = BookingId,
            PageTitle = "Book a live demo",
            Language = "en",
            VisitorName = "Jane Doe",
            VisitorEmail = "jane@acme-tax.com",
            ExpiresAtUtc = Slot.AddDays(-6),
            Send = 1,
            ProtectedCode = _codec.Protect(
                PublicBookingPayloadSchemas.VerificationCode,
                PublicBookingPayloadSchemas.CodeBinding(Tenant, BookingId, 1),
                new PublicBookingSecretPayload(code)
            ),
        };

    private ProtectedPayload ProtectedToken(int version) =>
        _codec.Protect(
            PublicBookingPayloadSchemas.ManagementToken,
            PublicBookingPayloadSchemas.ManagementTokenBinding(Tenant, BookingId, version),
            new PublicBookingSecretPayload(Token)
        );

    private static PublicBookingVisitor Visitor =>
        new("Jane Doe", "jane@acme-tax.com", "Acme Tax", "TwoToFive", null, "E-signatures", "America/New_York");

    private static PublicBookingHost Host =>
        new(
            Guid.NewGuid(),
            "Ana Pérez",
            "demos@taxoffix.io",
            "https://meet.google.com/abc-defg-hij",
            "America/Los_Angeles"
        );

    private PublicBookingConfirmedIntegrationEvent Confirmed() =>
        new()
        {
            TenantId = Tenant,
            BookingId = BookingId,
            AppointmentId = Guid.NewGuid(),
            PageTitle = "Book a live demo",
            StartUtc = Slot,
            DurationMinutes = 30,
            Language = "en",
            Visitor = Visitor,
            Host = Host,
            HeardAbout = new PublicBookingHeardAbout("whatsapp", "WhatsApp", "WhatsApp", null),
            Attribution = new PublicBookingAttribution("Hero", "google", "cpc", "fall", "google.com"),
            MarketingOptIn = false,
            IcsUid = $"public-booking-{BookingId:N}",
            IcsSequence = 0,
            IcsAlarmMinutes = [1440, 60],
            ManagementTokenVersion = 1,
            ProtectedManagementToken = ProtectedToken(1),
        };

    private PublicBookingRescheduledIntegrationEvent Rescheduled() =>
        new()
        {
            TenantId = Tenant,
            BookingId = BookingId,
            AppointmentId = Guid.NewGuid(),
            PageTitle = "Book a live demo",
            PreviousStartUtc = Slot.AddDays(-1),
            StartUtc = Slot,
            DurationMinutes = 30,
            Language = "en",
            Visitor = Visitor,
            Host = Host,
            ChangedBy = "Staff",
            Reason = "Host conflict",
            IcsUid = $"public-booking-{BookingId:N}",
            IcsSequence = 2,
            IcsAlarmMinutes = [1440, 60],
            ManagementTokenVersion = 3,
            ProtectedManagementToken = ProtectedToken(3),
        };

    private PublicBookingCancelledIntegrationEvent Cancelled() =>
        new()
        {
            TenantId = Tenant,
            BookingId = BookingId,
            PageTitle = "Book a live demo",
            StartUtc = Slot,
            DurationMinutes = 30,
            Language = "en",
            Visitor = Visitor,
            Host = Host,
            CancelledBy = "Visitor",
            Reason = null,
            IcsUid = $"public-booking-{BookingId:N}",
            IcsSequence = 3,
        };

    private PublicBookingAlreadyBookedIntegrationEvent AlreadyBooked() =>
        new()
        {
            TenantId = Tenant,
            BookingId = BookingId,
            PageTitle = "Agenda una demo en vivo",
            StartUtc = Slot,
            DurationMinutes = 30,
            Language = "es",
            Visitor = Visitor,
            Host = Host,
            ManagementTokenVersion = 4,
            ProtectedManagementToken = ProtectedToken(4),
        };

    private PublicBookingReminderDueIntegrationEvent Reminder() =>
        new()
        {
            TenantId = Tenant,
            BookingId = BookingId,
            PageTitle = "Book a live demo",
            StartUtc = Slot,
            DurationMinutes = 30,
            Language = "en",
            Visitor = Visitor,
            Host = Host,
            HoursBefore = 24,
            ManagementTokenVersion = 2,
            ProtectedManagementToken = ProtectedToken(2),
        };

    private static string Ics(EmailDispatchRequest email) =>
        Encoding.UTF8.GetString(Convert.FromBase64String(Assert.Single(email.InlineAttachments!).ContentBase64));

    private sealed class RecordingScribe : IScribeRenderClient
    {
        private readonly List<(string EventKey, IReadOnlyDictionary<string, object?> Variables)> _calls = [];

        public IReadOnlyDictionary<string, object?> Variables(string eventKey, string? recipient = null) =>
            _calls
                .Last(c =>
                    c.EventKey == eventKey && (recipient is null || Equals(c.Variables["recipient_email"], recipient))
                )
                .Variables;

        public Task<Result<ScribeRenderedEmail>> RenderAsync(
            string eventKey,
            Guid tenantId,
            IReadOnlyDictionary<string, object?> variables,
            CancellationToken ct = default
        ) => RenderAsync(eventKey, tenantId, variables, ScribeRenderAudience.Staff, ct);

        public Task<Result<ScribeRenderedEmail>> RenderAsync(
            string eventKey,
            Guid tenantId,
            IReadOnlyDictionary<string, object?> variables,
            ScribeRenderAudience audience,
            CancellationToken ct = default
        )
        {
            _calls.Add((eventKey, variables));
            return Task.FromResult(Result.Success(new ScribeRenderedEmail(eventKey, "<p>html</p>", "text")));
        }

        public Task<Result<ScribeRenderedText>> RenderTextAsync(
            string eventKey,
            Guid tenantId,
            IReadOnlyDictionary<string, object?> variables,
            CancellationToken ct = default
        ) => throw new NotSupportedException();
    }

    /// <summary>Como el gateway real: deduplica por evento, plantilla y destinatario, y deja el registro.</summary>
    private sealed class RecordingGateway(MemoryLogs logs) : IEmailDispatchGateway
    {
        public List<EmailDispatchRequest> Queued { get; } = [];

        public async Task<EmailDispatchResult> QueueEmailAsync(
            EmailDispatchRequest request,
            CancellationToken ct = default
        )
        {
            if (
                request.RelatedEventId is { } related
                && await logs.GetByRelatedEventIdAsync(request.TenantId, related, request.TemplateKey, request.To, ct)
                    is not null
            )
                return new EmailDispatchResult(
                    Guid.Empty,
                    Guid.Empty,
                    NotificationDispatchAttemptStatus.Queued,
                    null,
                    null
                );

            Queued.Add(request);
            logs.Record(request);
            return new EmailDispatchResult(
                Guid.NewGuid(),
                Guid.NewGuid(),
                NotificationDispatchAttemptStatus.Queued,
                null,
                null
            );
        }
    }

    private sealed class MemoryLogs : INotificationLogRepository
    {
        private readonly List<NotificationLog> _logs = [];

        public void Record(EmailDispatchRequest request) =>
            _logs.Add(
                NotificationLog
                    .Create(
                        request.TenantId,
                        NotificationChannel.Email,
                        NotificationRecipient.ForEmail(request.To).Value,
                        request.Subject,
                        request.TemplateKey,
                        request.RelatedEventId,
                        request.CorrelationId
                    )
                    .Value
            );

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
        ) =>
            Task.FromResult(
                _logs.Find(l =>
                    l.TenantId == tenantId
                    && l.RelatedEventId == relatedEventId
                    && l.TemplateKey == templateKey
                    && string.Equals(l.Recipient, recipient, StringComparison.OrdinalIgnoreCase)
                )
            );

        public Task<NotificationLog?> GetByRelatedEventAsync(
            Guid tenantId,
            Guid relatedEventId,
            string kind,
            NotificationChannel channel,
            string recipient,
            CancellationToken ct = default
        ) => Task.FromResult<NotificationLog?>(null);
    }

    private sealed class Correlation : ICorrelationContext
    {
        public string CorrelationId { get; private set; } = "corr";

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
