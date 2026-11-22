using BuildingBlocks.Messaging.EmailIntegrationEvents;
using BuildingBlocks.Platform;
using BuildingBlocks.TimeZones;
using BuildingBlocks.TimeZones.Formatting;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;

namespace TaxVision.Notification.Application.Consumers.Communication;

/// <summary>
/// Lo que comparten los avisos de reunión (F6): hora local con la zona de la oficina, el in-app a un
/// conjunto de usuarios y el <c>.ics</c>. El correo lo filtra el gate del gateway (categoría
/// <see cref="NotificationCategory.Calendar"/>); un externo sin cuenta lo recibe siempre.
/// </summary>
internal static class MeetingNotifications
{
    /// <summary>Hora para la campana con el formato único (P15) en la zona de la reunión; sin zona válida, "UTC".</summary>
    public static string? Describe(DateTime? utc, string? timeZone) =>
        utc is { } value ? ZonedTimeFormatter.DateTime(value, ResolvedTimeZone.Resolve(timeZone)) : null;

    /// <summary>Rótulo legible del kit ("New York time, GMT-5"), en el idioma del lector; sin zona válida, el de UTC.</summary>
    public static string TimeZoneLabel(string? timeZone, DateTime? atUtc, string? language = null) =>
        ZonedTimeFormatter.ZoneLabel(ResolvedTimeZone.Resolve(timeZone), atUtc ?? DateTime.UtcNow, language);

    public static string Title(string? meetingTitle) =>
        string.IsNullOrWhiteSpace(meetingTitle) ? "Meeting" : meetingTitle.Trim();

    /// <summary>Título para el correo: sin título, "Reunión"/"Meeting" en el idioma del lector.</summary>
    public static string EmailTitle(string? meetingTitle, RecipientLocale locale) =>
        string.IsNullOrWhiteSpace(meetingTitle) ? EmailText.For("meeting.untitled", locale) : meetingTitle.Trim();

    /// <summary>Externo sin cuenta (el evento no trae su idioma): inglés, con la hora en la zona de la reunión.</summary>
    public static RecipientLocale ExternalLocale(IRecipientLocale locales, string? timeZone) =>
        locales.FromEventLanguage(null) with
        {
            TimeZoneId = string.IsNullOrWhiteSpace(timeZone) ? null : timeZone,
        };

    /// <summary>In-app a cada usuario (sin repetir y sin el actor, que ya sabe lo que hizo).</summary>
    public static async Task PublishInAppAsync(
        NotificationDispatcher dispatcher,
        IEnumerable<Guid> userIds,
        Guid? exclude,
        Guid tenantId,
        Guid meetingId,
        Guid sourceEventId,
        string kind,
        string sourceEventType,
        DateTime occurredAtUtc,
        string title,
        string? body,
        string? correlationId,
        IReadOnlyDictionary<string, string>? context,
        CancellationToken ct,
        IReadOnlyDictionary<string, string>? parameters = null
    )
    {
        foreach (var userId in userIds.Distinct())
        {
            if (userId == Guid.Empty || userId == exclude)
                continue;

            await dispatcher.DispatchAsync(
                new NotificationIntent(
                    kind,
                    tenantId,
                    new NotificationTarget(userId),
                    sourceEventId,
                    sourceEventType,
                    occurredAtUtc,
                    correlationId,
                    InApp: new InAppContent(
                        Params: parameters,
                        ResourceId: meetingId.ToString(),
                        ResourceContext: context,
                        FallbackTitle: title,
                        FallbackBody: body
                    )
                ),
                ct
            );
        }
    }

    /// <summary>Correo ya renderizado (una vez para todos los externos o por invitado), con su .ics si lo hay.</summary>
    public static EmailContent Rendered(
        string eventKey,
        ScribeRenderedEmail render,
        EmailInlineAttachment? invite,
        ScribeRenderAudience audience = ScribeRenderAudience.Client,
        string? templateKey = null
    ) =>
        new(
            eventKey,
            NoVariables,
            audience,
            TemplateKey: templateKey,
            Render: (_, _) => Task.FromResult(render),
            InlineAttachments: invite is null ? null : [invite]
        );

    private static readonly IReadOnlyDictionary<string, object?> NoVariables = new Dictionary<string, object?>();

    /// <summary>
    /// El <c>.ics</c> de la reunión para un destinatario. Organizador = el host con el correo de soporte
    /// de la plataforma: Notification no conoce el remitente real (lo resuelve Postmaster) y el evento no
    /// trae el correo del host.
    /// </summary>
    public static CalendarInviteRequest Invite(
        CalendarInviteMethod method,
        Guid meetingId,
        string? meetingTitle,
        DateTime startUtc,
        int? durationMinutes,
        int sequence,
        DateTime stampUtc,
        string? url,
        string? meetingCode,
        string? hostDisplayName,
        IPlatformSettings platform,
        string attendeeEmail,
        string? attendeeName
    ) =>
        new(
            method,
            meetingId,
            Title(meetingTitle),
            startUtc,
            durationMinutes,
            sequence,
            stampUtc,
            url,
            meetingCode,
            string.IsNullOrWhiteSpace(hostDisplayName) ? platform.Current.BrandName : hostDisplayName,
            platform.Current.SupportEmail,
            attendeeEmail,
            attendeeName
        );

    /// <summary>SEQUENCE que crece con el tiempo (minutos desde la época): cada cambio supera al anterior.</summary>
    public static int SequenceAt(DateTime utc) =>
        (int)
            Math.Clamp(
                (DateTime.SpecifyKind(utc, DateTimeKind.Utc) - DateTime.UnixEpoch).TotalMinutes,
                1,
                int.MaxValue
            );
}
