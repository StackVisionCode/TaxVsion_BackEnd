using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CalendarIntegrationEvents;
using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using BuildingBlocks.Messaging.EmailIntegrationEvents;
using BuildingBlocks.Platform;
using Microsoft.Extensions.Logging;
using TaxVision.Notification.Application.Abstractions;

namespace TaxVision.Notification.Application.Consumers.Calendar.PublicBookings;

/// <summary>
/// Los correos de la demo pública del landing (DEMO-09 §7). Cuidados de consumidor (guía §7.4–7.5):
/// <list type="bullet">
/// <item>Idempotencia: cada correo usa una clave semántica estable (<see cref="KeyFor"/>) como evento relacionado;
/// el gateway deduplica por esa clave, la plantilla y el destinatario, así que una reentrega o un evento repetido
/// con otro id no manda dos correos.</item>
/// <item>Orden: la cancelación es terminal y un aviso que llega después no sale. Cada cambio sube el
/// <c>SEQUENCE</c> en uno: si ya salió el aviso del cambio siguiente, el viejo tampoco sale.</item>
/// <item>Secretos: el código y el enlace de gestión se descifran solo con su atadura; si no abren se registra y no
/// se reintenta (reintentar no lo arregla). Los correos que los llevan salen confidenciales.</item>
/// </list>
/// Primero se renderiza (HTTP a Scribe) y recién después se encola: el render no queda dentro de la escritura.
/// </summary>
public sealed class PublicBookingEmails(
    IScribeRenderClient scribe,
    IEmailDispatchGateway gateway,
    ICalendarInviteBuilder invites,
    IProtectedPayloadCodec codec,
    IPlatformSettings platform,
    INotificationLogRepository logs,
    ICorrelationContext correlation,
    ILogger<PublicBookingEmails> logger
)
{
    public const string CodeTemplate = "demo.verification_code.v1";
    public const string ConfirmedTemplate = "demo.confirmed.v1";
    public const string NewBookingTemplate = "demo.new_booking.v1";
    public const string RescheduledTemplate = "demo.rescheduled.v1";
    public const string CancelledTemplate = "demo.cancelled.v1";
    public const string ReminderTemplate = "demo.reminder.v1";
    public const string AlreadyBookedTemplate = "demo.already_booked.v1";
    internal const string Spanish = "Es";
    private const string English = "En";

    /// <summary>Id estable de un correo de la reserva: el mismo hecho produce siempre el mismo.</summary>
    public static Guid KeyFor(Guid bookingId, string suffix)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes($"calendar.public_booking:{bookingId:N}:{suffix}"));
        return new Guid(hash.AsSpan(0, 16));
    }

    public async Task SendCodeAsync(PublicBookingCodeRequestedIntegrationEvent evt, CancellationToken ct)
    {
        using var scope = correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId));
        var code = Open(
            evt.ProtectedCode,
            PublicBookingPayloadSchemas.VerificationCode,
            PublicBookingPayloadSchemas.CodeBinding(evt.TenantId, evt.BookingId, evt.Send),
            evt.BookingId
        );
        if (code is null)
            return;

        var language = Language(evt.Language);
        var render = await RenderAsync(
            CodeTemplate,
            evt.TenantId,
            new Dictionary<string, object?>
            {
                ["language"] = language,
                ["recipient_email"] = evt.VisitorEmail,
                ["first_name"] = FirstName(evt.VisitorName),
                ["code"] = code,
                ["expires_minutes"] = Math.Max(1, (int)Math.Round((evt.ExpiresAtUtc - DateTime.UtcNow).TotalMinutes)),
                ["page_title"] = evt.PageTitle,
            },
            ct
        );
        await QueueAsync(
            render,
            evt.TenantId,
            evt.VisitorEmail,
            CodeTemplate,
            KeyFor(evt.BookingId, $"code:{evt.Send}"),
            confidential: true,
            invite: null,
            ct
        );
    }

    public async Task SendConfirmedAsync(PublicBookingConfirmedIntegrationEvent evt, CancellationToken ct)
    {
        using var scope = correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId));
        if (
            await IsCancelledAsync(evt.TenantId, evt.BookingId, evt.Visitor.Email, ct)
            || await IsOutdatedAsync(evt.TenantId, evt.BookingId, evt.Visitor.Email, evt.IcsSequence, ct)
        )
            return;
        var token = OpenToken(evt.ProtectedManagementToken, evt.TenantId, evt.BookingId, evt.ManagementTokenVersion);
        if (token is null)
            return;

        var demo = new Demo(
            evt.BookingId,
            evt.PageTitle,
            evt.StartUtc,
            evt.DurationMinutes,
            evt.Visitor,
            evt.Host,
            evt.IcsUid,
            evt.IcsSequence,
            evt.IcsAlarmMinutes
        );
        var visitorVariables = VisitorVariables(demo, Language(evt.Language), token);
        var hostVariables = HostVariables(demo);
        hostVariables["heard_about"] = evt.HeardAbout.LabelEn;
        hostVariables["heard_about_detail"] = evt.HeardAbout.Detail;
        hostVariables["entry_source"] = evt.Attribution.EntrySource;
        hostVariables["utm_source"] = evt.Attribution.UtmSource;
        hostVariables["utm_campaign"] = evt.Attribution.UtmCampaign;
        hostVariables["referrer_domain"] = evt.Attribution.ReferrerDomain;
        hostVariables["marketing_opt_in"] = evt.MarketingOptIn;

        // Los dos renders antes de encolar: si Scribe falla, no queda un correo a medias.
        var forVisitor = await RenderAsync(ConfirmedTemplate, evt.TenantId, visitorVariables, ct);
        var forHost = await RenderAsync(NewBookingTemplate, evt.TenantId, hostVariables, ct);
        var version = evt.ManagementTokenVersion;
        await QueueAsync(
            forVisitor,
            evt.TenantId,
            evt.Visitor.Email,
            ConfirmedTemplate,
            KeyFor(evt.BookingId, $"confirmed:v{version}"),
            true,
            Invite(demo, CalendarInviteMethod.Request, forHost: false),
            ct
        );
        await QueueAsync(
            forHost,
            evt.TenantId,
            evt.Host.NotificationEmail,
            NewBookingTemplate,
            KeyFor(evt.BookingId, $"confirmed:host:v{version}"),
            false,
            Invite(demo, CalendarInviteMethod.Request, forHost: true),
            ct
        );
    }

    public async Task SendRescheduledAsync(PublicBookingRescheduledIntegrationEvent evt, CancellationToken ct)
    {
        using var scope = correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId));
        if (
            await IsCancelledAsync(evt.TenantId, evt.BookingId, evt.Visitor.Email, ct)
            || await IsOutdatedAsync(evt.TenantId, evt.BookingId, evt.Visitor.Email, evt.IcsSequence, ct)
        )
            return;
        var token = OpenToken(evt.ProtectedManagementToken, evt.TenantId, evt.BookingId, evt.ManagementTokenVersion);
        if (token is null)
            return;

        var demo = new Demo(
            evt.BookingId,
            evt.PageTitle,
            evt.StartUtc,
            evt.DurationMinutes,
            evt.Visitor,
            evt.Host,
            evt.IcsUid,
            evt.IcsSequence,
            evt.IcsAlarmMinutes
        );
        var visitorVariables = VisitorVariables(demo, Language(evt.Language), token);
        var hostVariables = HostVariables(demo);
        foreach (
            var (variables, zone, language) in new[]
            {
                (visitorVariables, evt.Visitor.TimeZoneId, Language(evt.Language)),
                (hostVariables, evt.Host.TimeZoneId, English),
            }
        )
        {
            var previous = PublicBookingTime.Describe(evt.PreviousStartUtc, zone, language);
            variables["previous_date_local"] = previous.Date;
            variables["previous_time_local"] = previous.Time;
            variables["changed_by"] = evt.ChangedBy;
            variables["reason"] = evt.Reason;
        }

        var forVisitor = await RenderAsync(RescheduledTemplate, evt.TenantId, visitorVariables, ct);
        var forHost = await RenderAsync(RescheduledTemplate, evt.TenantId, hostVariables, ct);
        await QueueAsync(
            forVisitor,
            evt.TenantId,
            evt.Visitor.Email,
            RescheduledTemplate,
            KeyFor(evt.BookingId, $"rescheduled:{evt.IcsSequence}:visitor"),
            true,
            Invite(demo, CalendarInviteMethod.Request, forHost: false),
            ct
        );
        await QueueAsync(
            forHost,
            evt.TenantId,
            evt.Host.NotificationEmail,
            RescheduledTemplate,
            KeyFor(evt.BookingId, $"rescheduled:{evt.IcsSequence}:host"),
            false,
            Invite(demo, CalendarInviteMethod.Request, forHost: true),
            ct
        );
    }

    public async Task SendCancelledAsync(PublicBookingCancelledIntegrationEvent evt, CancellationToken ct)
    {
        using var scope = correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId));
        var demo = new Demo(
            evt.BookingId,
            evt.PageTitle,
            evt.StartUtc,
            evt.DurationMinutes,
            evt.Visitor,
            evt.Host,
            evt.IcsUid,
            evt.IcsSequence,
            []
        );
        var visitorVariables = VisitorVariables(demo, Language(evt.Language), managementToken: null);
        var hostVariables = HostVariables(demo);
        foreach (var variables in new[] { visitorVariables, hostVariables })
        {
            variables["cancelled_by"] = evt.CancelledBy;
            variables["reason"] = evt.Reason;
        }
        visitorVariables["book_again_url"] = platform.Current.LandingUrl("/demo");

        var forVisitor = await RenderAsync(CancelledTemplate, evt.TenantId, visitorVariables, ct);
        var forHost = await RenderAsync(CancelledTemplate, evt.TenantId, hostVariables, ct);
        // La cancelación es una sola por reserva: su clave no lleva versión y marca el estado terminal.
        await QueueAsync(
            forVisitor,
            evt.TenantId,
            evt.Visitor.Email,
            CancelledTemplate,
            KeyFor(evt.BookingId, "cancelled:visitor"),
            false,
            Invite(demo, CalendarInviteMethod.Cancel, forHost: false),
            ct
        );
        await QueueAsync(
            forHost,
            evt.TenantId,
            evt.Host.NotificationEmail,
            CancelledTemplate,
            KeyFor(evt.BookingId, "cancelled:host"),
            false,
            Invite(demo, CalendarInviteMethod.Cancel, forHost: true),
            ct
        );
    }

    /// <summary>
    /// Pidió otra demo teniendo una: se le recuerda la suya con el enlace nuevo. Lleva el secreto, así que sale
    /// confidencial; la clave por versión del enlace evita mandar el mismo aviso dos veces.
    /// </summary>
    public async Task SendAlreadyBookedAsync(PublicBookingAlreadyBookedIntegrationEvent evt, CancellationToken ct)
    {
        using var scope = correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId));
        if (await IsCancelledAsync(evt.TenantId, evt.BookingId, evt.Visitor.Email, ct))
            return;
        var token = OpenToken(evt.ProtectedManagementToken, evt.TenantId, evt.BookingId, evt.ManagementTokenVersion);
        if (token is null)
            return;

        var demo = new Demo(
            evt.BookingId,
            evt.PageTitle,
            evt.StartUtc,
            evt.DurationMinutes,
            evt.Visitor,
            evt.Host,
            PublicBookingUid(evt.BookingId),
            0,
            []
        );
        var variables = VisitorVariables(demo, Language(evt.Language), token);
        var render = await RenderAsync(AlreadyBookedTemplate, evt.TenantId, variables, ct);
        await QueueAsync(
            render,
            evt.TenantId,
            evt.Visitor.Email,
            AlreadyBookedTemplate,
            KeyFor(evt.BookingId, $"already:v{evt.ManagementTokenVersion}"),
            true,
            invite: null,
            ct
        );
    }

    public async Task SendReminderAsync(PublicBookingReminderDueIntegrationEvent evt, CancellationToken ct)
    {
        using var scope = correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId));
        if (await IsCancelledAsync(evt.TenantId, evt.BookingId, evt.Visitor.Email, ct))
            return;
        var token = OpenToken(evt.ProtectedManagementToken, evt.TenantId, evt.BookingId, evt.ManagementTokenVersion);
        if (token is null)
            return;

        var demo = new Demo(
            evt.BookingId,
            evt.PageTitle,
            evt.StartUtc,
            evt.DurationMinutes,
            evt.Visitor,
            evt.Host,
            PublicBookingUid(evt.BookingId),
            0,
            []
        );
        var variables = VisitorVariables(demo, Language(evt.Language), token);
        variables["hours_before"] = evt.HoursBefore;
        var render = await RenderAsync(ReminderTemplate, evt.TenantId, variables, ct);
        await QueueAsync(
            render,
            evt.TenantId,
            evt.Visitor.Email,
            ReminderTemplate,
            KeyFor(evt.BookingId, $"reminder:v{evt.ManagementTokenVersion}"),
            true,
            invite: null,
            ct
        );
    }

    // ---------- Partes ----------

    private sealed record Demo(
        Guid BookingId,
        string PageTitle,
        DateTime StartUtc,
        int DurationMinutes,
        PublicBookingVisitor Visitor,
        PublicBookingHost Host,
        string IcsUid,
        int IcsSequence,
        IReadOnlyList<int> AlarmMinutes
    );

    private Dictionary<string, object?> VisitorVariables(Demo demo, string language, string? managementToken)
    {
        var local = PublicBookingTime.Describe(demo.StartUtc, demo.Visitor.TimeZoneId, language);
        var variables = new Dictionary<string, object?>
        {
            ["language"] = language,
            ["is_host"] = false,
            ["recipient_email"] = demo.Visitor.Email,
            ["first_name"] = FirstName(demo.Visitor.Name),
            ["page_title"] = demo.PageTitle,
            ["host_name"] = demo.Host.DisplayName,
            ["date_local"] = local.Date,
            ["time_local"] = local.Time,
            ["time_zone_label"] = local.ZoneLabel,
            ["duration_minutes"] = demo.DurationMinutes,
            ["meeting_url"] = demo.Host.MeetingUrl,
            ["google_calendar_url"] = GoogleCalendarUrl(demo, VisitorSummary(demo)),
            ["outlook_calendar_url"] = OutlookCalendarUrl(demo, VisitorSummary(demo)),
        };
        if (managementToken is not null)
            variables["manage_url"] =
                $"{platform.Current.LandingUrl("/demo/manage")}#token={Uri.EscapeDataString(managementToken)}";
        return variables;
    }

    /// <summary>El anfitrión lee la demo en su zona y en inglés (equipo de la plataforma), con los datos del prospecto.</summary>
    private static Dictionary<string, object?> HostVariables(Demo demo)
    {
        var local = PublicBookingTime.Describe(demo.StartUtc, demo.Host.TimeZoneId, English);
        var visitorLocal = PublicBookingTime.Describe(demo.StartUtc, demo.Visitor.TimeZoneId, English);
        return new Dictionary<string, object?>
        {
            ["language"] = English,
            ["is_host"] = true,
            ["recipient_email"] = demo.Host.NotificationEmail,
            ["first_name"] = FirstName(demo.Host.DisplayName),
            ["page_title"] = demo.PageTitle,
            ["host_name"] = demo.Host.DisplayName,
            ["date_local"] = local.Date,
            ["time_local"] = local.Time,
            ["time_zone_label"] = local.ZoneLabel,
            ["visitor_time_local"] = $"{visitorLocal.Time} {visitorLocal.ZoneLabel}",
            ["duration_minutes"] = demo.DurationMinutes,
            ["meeting_url"] = demo.Host.MeetingUrl,
            ["visitor_name"] = demo.Visitor.Name,
            ["visitor_email"] = demo.Visitor.Email,
            ["company"] = demo.Visitor.Company,
            ["office_size"] = demo.Visitor.OfficeSize,
            ["phone"] = demo.Visitor.Phone,
            ["interest"] = demo.Visitor.Interest,
        };
    }

    private EmailInlineAttachment Invite(Demo demo, CalendarInviteMethod method, bool forHost) =>
        invites.Build(
            new CalendarInviteRequest(
                method,
                demo.BookingId,
                forHost ? HostSummary(demo) : VisitorSummary(demo),
                demo.StartUtc,
                demo.DurationMinutes,
                demo.IcsSequence,
                DateTime.UtcNow,
                Url: demo.Host.MeetingUrl,
                MeetingCode: null,
                OrganizerName: demo.Host.DisplayName,
                OrganizerEmail: platform.Current.SupportEmail,
                AttendeeEmail: demo.Visitor.Email,
                AttendeeName: demo.Visitor.Name,
                UidBase: demo.IcsUid,
                Location: demo.Host.MeetingUrl,
                Description: forHost ? $"Demo with {demo.Visitor.Name} ({demo.Visitor.Company})." : null,
                AlarmMinutes: demo.AlarmMinutes
            )
        );

    private string VisitorSummary(Demo demo) => $"{platform.Current.BrandName} demo with {demo.Host.DisplayName}";

    private static string HostSummary(Demo demo) => $"Demo · {demo.Visitor.Company} ({demo.Visitor.Name})";

    private static string GoogleCalendarUrl(Demo demo, string title) =>
        "https://calendar.google.com/calendar/render?action=TEMPLATE"
        + $"&text={Uri.EscapeDataString(title)}"
        + $"&dates={Stamp(demo.StartUtc)}/{Stamp(demo.StartUtc.AddMinutes(demo.DurationMinutes))}"
        + $"&details={Uri.EscapeDataString($"Join: {demo.Host.MeetingUrl}")}"
        + $"&location={Uri.EscapeDataString(demo.Host.MeetingUrl)}";

    private static string OutlookCalendarUrl(Demo demo, string title) =>
        "https://outlook.live.com/calendar/0/deeplink/compose?path=%2Fcalendar%2Faction%2Fcompose&rru=addevent"
        + $"&subject={Uri.EscapeDataString(title)}"
        + $"&startdt={Uri.EscapeDataString(demo.StartUtc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture))}"
        + $"&enddt={Uri.EscapeDataString(demo.StartUtc.AddMinutes(demo.DurationMinutes).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture))}"
        + $"&body={Uri.EscapeDataString($"Join: {demo.Host.MeetingUrl}")}"
        + $"&location={Uri.EscapeDataString(demo.Host.MeetingUrl)}";

    private static string Stamp(DateTime utc) =>
        DateTime
            .SpecifyKind(utc, DateTimeKind.Utc)
            .ToString("yyyyMMdd'T'HHmmss'Z'", System.Globalization.CultureInfo.InvariantCulture);

    private static string PublicBookingUid(Guid bookingId) => $"public-booking-{bookingId:N}";

    private static string Language(string? language) =>
        string.Equals(language, "es", StringComparison.OrdinalIgnoreCase) ? Spanish : English;

    private static string FirstName(string? fullName)
    {
        var trimmed = fullName?.Trim() ?? string.Empty;
        var space = trimmed.IndexOf(' ');
        return space > 0 ? trimmed[..space] : trimmed;
    }

    /// <summary>La cancelación ya salió: es terminal, y un aviso que llega tarde no debe contradecirla.</summary>
    private async Task<bool> IsCancelledAsync(Guid tenantId, Guid bookingId, string visitorEmail, CancellationToken ct)
    {
        var cancelled = await logs.GetByRelatedEventIdAsync(
            tenantId,
            KeyFor(bookingId, "cancelled:visitor"),
            CancelledTemplate,
            visitorEmail,
            ct
        );
        if (cancelled is not null)
            logger.LogInformation(
                "Public booking {BookingId}: already cancelled, a late notice is not sent.",
                bookingId
            );
        return cancelled is not null;
    }

    /// <summary>Ya salió el aviso del cambio siguiente: este llega tarde y mandaría la hora anterior.</summary>
    private async Task<bool> IsOutdatedAsync(
        Guid tenantId,
        Guid bookingId,
        string visitorEmail,
        int sequence,
        CancellationToken ct
    )
    {
        var next = await logs.GetByRelatedEventIdAsync(
            tenantId,
            KeyFor(bookingId, $"rescheduled:{sequence + 1}:visitor"),
            RescheduledTemplate,
            visitorEmail,
            ct
        );
        if (next is not null)
            logger.LogInformation(
                "Public booking {BookingId}: a newer move was already sent, notice {Sequence} is skipped.",
                bookingId,
                sequence
            );
        return next is not null;
    }

    private string? OpenToken(ProtectedPayload payload, Guid tenantId, Guid bookingId, int version) =>
        Open(
            payload,
            PublicBookingPayloadSchemas.ManagementToken,
            PublicBookingPayloadSchemas.ManagementTokenBinding(tenantId, bookingId, version),
            bookingId
        );

    private string? Open(ProtectedPayload payload, string schema, ProtectedPayloadBinding binding, Guid bookingId)
    {
        try
        {
            return codec.Unprotect<PublicBookingSecretPayload>(payload, schema, binding).Value;
        }
        // Solo lo permanente se descarta; un fallo transitorio del anillo de claves sube y Wolverine reintenta.
        catch (Exception ex)
            when (ex is CryptographicException or FormatException or JsonException or ArgumentException)
        {
            logger.LogError(
                ex,
                "Public booking {BookingId}: the {Schema} secret could not be decrypted.",
                bookingId,
                schema
            );
            return null;
        }
    }

    private async Task<ScribeRenderedEmail> RenderAsync(
        string templateKey,
        Guid tenantId,
        IReadOnlyDictionary<string, object?> variables,
        CancellationToken ct
    ) =>
        (await scribe.RenderAsync(templateKey, tenantId, variables, ScribeRenderAudience.Client, ct)).EnsureRendered(
            templateKey
        );

    private Task QueueAsync(
        ScribeRenderedEmail render,
        Guid tenantId,
        string to,
        string templateKey,
        Guid relatedEventId,
        bool confidential,
        EmailInlineAttachment? invite,
        CancellationToken ct
    ) =>
        gateway.QueueEmailAsync(
            render.ToDispatchRequest(tenantId, to, templateKey, relatedEventId, correlation.CorrelationId) with
            {
                // Con código o enlace de gestión: cola privada de Postmaster, no el fanout.
                Confidential = confidential,
                InlineAttachments = invite is null ? null : [invite],
            },
            ct
        );
}
