using BuildingBlocks.Platform;
using Microsoft.Extensions.Logging;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Application.Consumers.Calendar;
using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Domain.Preferences;

namespace TaxVision.Notification.Application.Consumers.Communication;

/// <summary>
/// Lo que comparten los avisos de reunión (F6): hora local con la zona de la oficina, el gate de
/// preferencia (categoría <see cref="NotificationCategory.Calendar"/>, como las citas), el in-app a un
/// conjunto de usuarios y el <c>.ics</c>. Un externo sin cuenta no tiene preferencia ni bandeja: recibe
/// el correo siempre.
/// </summary>
internal static class MeetingNotifications
{
    /// <summary>Hora en la zona de la oficina; sin zona (o zona inválida) sale en UTC y lo dice.</summary>
    public static string? LocalTime(DateTime? utc, string? timeZone) =>
        utc is { } value ? CalendarTimeFormatting.InZone(value, timeZone ?? string.Empty) : null;

    public static string TimeZoneLabel(string? timeZone) =>
        string.IsNullOrWhiteSpace(timeZone) ? "UTC" : timeZone.Trim();

    public static string Title(string? meetingTitle) =>
        string.IsNullOrWhiteSpace(meetingTitle) ? "Meeting" : meetingTitle.Trim();

    public static async Task<bool> EmailAllowedAsync(
        IUserNotificationPreferenceRepository preferences,
        Guid tenantId,
        Guid? userId,
        ILogger logger,
        string templateKey,
        CancellationToken ct
    )
    {
        if (userId is not { } id)
            return true;

        var enabled = await preferences.IsEnabledAsync(
            tenantId,
            id,
            NotificationCategory.Calendar,
            NotificationChannel.Email,
            ct
        );
        if (!enabled)
            logger.LogInformation(
                "Meeting email suppressed by user preference: tenant {TenantId}, user {UserId}, template {TemplateKey}.",
                tenantId,
                id,
                templateKey
            );

        return enabled;
    }

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
        CancellationToken ct
    )
    {
        foreach (var userId in userIds.Distinct())
        {
            if (userId == Guid.Empty || userId == exclude)
                continue;

            await dispatcher.RecordInAppAsync(
                tenantId,
                $"user:{userId:N}",
                title,
                NotificationCategory.Calendar,
                kind,
                sourceEventId,
                correlationId,
                recipientUserId: userId,
                inApp: new InAppNotificationRequest(
                    kind,
                    userId,
                    sourceEventId,
                    sourceEventType,
                    occurredAtUtc,
                    title,
                    FallbackBody: body,
                    ResourceId: meetingId.ToString(),
                    ResourceContext: context
                ),
                ct: ct
            );
        }
    }

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
