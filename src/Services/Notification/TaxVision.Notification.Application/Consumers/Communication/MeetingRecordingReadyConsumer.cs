using System.Globalization;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CommunicationIntegrationEvents;
using Microsoft.Extensions.Options;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;

namespace TaxVision.Notification.Application.Consumers.Communication;

/// <summary>
/// La grabación de una reunión quedó lista: aviso en la app, correo y push, solo al anfitrión.
/// <list type="bullet">
/// <item>Cada canal respeta su preferencia (categoría Collaboration) y ninguno es obligatorio.</item>
/// <item>Sin dirección de correo, sale solo lo demás; el título nunca va en el push (pantalla bloqueada).</item>
/// <item>El push se agrupa por reunión y caduca en un día: un aviso viejo no sirve.</item>
/// </list>
/// </summary>
public static class MeetingRecordingReadyConsumer
{
    public const string EmailEventKey = "communication.meeting.recording_ready.v1";

    private const int MaxTitleLength = 120;
    private static readonly TimeSpan PushLifetime = TimeSpan.FromDays(1);

    public static async Task Handle(
        MeetingRecordingReadyIntegrationEvent evt,
        NotificationDispatcher dispatcher,
        IUserEmailResolver emails,
        ITenantHostResolver hosts,
        IOptions<PortalOptions> portal,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            var address = await emails.ResolveAsync(evt.TenantId, evt.HostUserId, ct);
            var host = await hosts.ResolveHostAsync(evt.TenantId, ct);
            var meetingUrl = $"{TenantEmailLinks.StaffBase(host, portal.Value)}/meetings?meetingId={evt.MeetingId}";
            await dispatcher.DispatchAsync(
                new NotificationIntent(
                    NotificationKinds.CommunicationMeetingRecordingReady,
                    evt.TenantId,
                    new NotificationTarget(evt.HostUserId, address),
                    evt.EventId,
                    EmailEventKey,
                    evt.ReadyAtUtc,
                    correlation.CorrelationId,
                    InApp: InApp(evt),
                    Push: new PushContent(
                        Tag: $"meeting-recording:{evt.MeetingId:N}",
                        TimeToLive: PushLifetime,
                        SkipWhenNoDevices: true
                    ),
                    Email: new EmailContent(EmailEventKey, EmailVariables(evt, meetingUrl))
                ),
                ct
            );
        }
    }

    // Igual que antes del correo: la fila de la bandeja deduplica por evento y no debe cambiar.
    private static InAppContent InApp(MeetingRecordingReadyIntegrationEvent evt) =>
        new(
            Params: new Dictionary<string, string>
            {
                ["durationSeconds"] = evt.DurationSeconds.ToString("F0", CultureInfo.InvariantCulture),
                ["participantCount"] = evt.ParticipantCount.ToString(CultureInfo.InvariantCulture),
            },
            ResourceId: evt.RecordingFileId.ToString(),
            ResourceContext: new Dictionary<string, string> { ["meetingId"] = evt.MeetingId.ToString() }
        );

    private static Dictionary<string, object?> EmailVariables(
        MeetingRecordingReadyIntegrationEvent evt,
        string meetingUrl
    ) =>
        new()
        {
            ["meeting_title"] = Title(evt.MeetingTitle),
            ["duration_minutes"] = Minutes(evt.DurationSeconds),
            ["ready_at"] = ScribeDates.Instant(evt.ReadyAtUtc),
            ["action_url"] = meetingUrl,
        };

    // El título lo escribe una persona: se recorta para el asunto; sin título la plantilla usa uno genérico.
    private static string? Title(string? title)
    {
        var trimmed = title?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return null;
        return trimmed.Length > MaxTitleLength ? trimmed[..MaxTitleLength].TrimEnd() + "…" : trimmed;
    }

    // Redondea hacia arriba y nunca muestra "0 min".
    private static int Minutes(double durationSeconds) => Math.Max(1, (int)Math.Ceiling(durationSeconds / 60));
}
