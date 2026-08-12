using BuildingBlocks.Common;
using BuildingBlocks.Messaging.ReminderIntegrationEvents;
using BuildingBlocks.TimeZones;
using Microsoft.Extensions.Options;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Preferences;

namespace TaxVision.Notification.Application.Consumers;

/// <summary>
/// <c>reminder.due.v1</c> — el recordatorio sonó y hay que avisarle a su dueño.
///
/// <para>
/// <b>Tres canales: in-app, push y email.</b> Los dos primeros se direccionan por <c>UserId</c>, que
/// es lo único que trae el evento — Reminder publica el hecho, no entrega (ADR-R-02). El email
/// necesitó primero el puente que la Fase 8 no tenía: <c>UserEmailResolver</c> traduce
/// <c>userId → email</c> contra el directorio local, con recuperación pull contra Auth para los
/// usuarios que existían antes de que esa tabla existiera.
/// </para>
///
/// <para>
/// <b>El correo va primero y es best-effort.</b> Sin dirección resoluble se omite ese canal y el
/// aviso igual sale por in-app y push: quedarse sin avisar por nada sería peor. Va primero porque es
/// el único de los tres que puede <b>lanzar</b> — si Scribe está caído, <c>EnsureRendered</c> lanza y
/// Wolverine reintenta el mensaje entero; con el correo al final, cada reintento repetiría el in-app
/// y el push que ya habían salido.
/// </para>
///
/// <para>
/// El gate de <see cref="NotificationCategory.Reminders"/> lo aplica el propio dispatcher, así que
/// apagar la categoría apaga los dos canales sin tocar este consumer.
/// </para>
/// </summary>
public static class ReminderDueConsumer
{
    private const string TemplateKey = "reminder.due";

    public static async Task Handle(
        ReminderDueIntegrationEvent evt,
        NotificationDispatcher dispatcher,
        UserEmailResolver emailResolver,
        IEmailDispatchGateway gateway,
        IScribeRenderClient scribeClient,
        IOptions<PortalOptions> portal,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            var title = evt.SnoozeCount > 0 ? $"{evt.Title} (pospuesto {evt.SnoozeCount})" : evt.Title;
            var body = BuildBody(evt);

            // El correo va PRIMERO, y no es un detalle de estilo: es el único de los tres canales que
            // puede lanzar (EnsureRendered, cuando Scribe está caído) y Wolverine reintenta el
            // mensaje ENTERO. Con el correo al final, cada reintento volvería a registrar el in-app y
            // a mandar el push — el usuario vería el mismo recordatorio repetido hasta la DLQ.
            await SendEmailAsync(evt, title, body, emailResolver, gateway, scribeClient, portal, correlation, ct);

            await dispatcher.RecordInAppAsync(
                evt.TenantId,
                evt.UserId.ToString("N"),
                title,
                NotificationCategory.Reminders,
                TemplateKey,
                evt.EventId,
                correlation.CorrelationId,
                recipientUserId: evt.UserId,
                ct: ct
            );

            await dispatcher.SendPushAsync(
                evt.TenantId,
                evt.UserId,
                title,
                body,
                NotificationCategory.Reminders,
                TemplateKey,
                evt.EventId,
                correlation.CorrelationId,
                ct
            );
        }
    }

    private static async Task SendEmailAsync(
        ReminderDueIntegrationEvent evt,
        string title,
        string body,
        UserEmailResolver emailResolver,
        IEmailDispatchGateway gateway,
        IScribeRenderClient scribeClient,
        IOptions<PortalOptions> portal,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var email = await emailResolver.ResolveAsync(evt.TenantId, evt.UserId, ct);
        if (email is null)
            return;

        var render = (
            await scribeClient.RenderAsync(
                "reminder.due.v1",
                evt.TenantId,
                new Dictionary<string, object?>
                {
                    ["title"] = title,
                    ["body"] = body,
                    ["category"] = evt.Category,
                    ["snooze_count"] = evt.SnoozeCount,
                    ["portal_link"] = portal.Value.BaseUrl.TrimEnd('/'),
                    ["product_name"] = portal.Value.ProductName,
                },
                ct
            )
        ).EnsureRendered("reminder.due.v1");

        await gateway.QueueEmailAsync(
            new EmailDispatchRequest(
                TenantId: evt.TenantId,
                To: email,
                Subject: render.Subject,
                HtmlBody: render.Html,
                TextBody: render.Text ?? string.Empty,
                TemplateKey: TemplateKey,
                RelatedEventId: evt.EventId,
                CorrelationId: correlation.CorrelationId,
                InlineAssets: render.InlineAssets
            ),
            ct
        );
    }

    /// <summary>
    /// El cuerpo que escribió el usuario si lo hay; si no, la hora del ancla <b>en su zona</b> —
    /// mostrarla en UTC sería inútil justo en el aviso donde la hora es todo el contenido.
    /// </summary>
    private static string BuildBody(ReminderDueIntegrationEvent evt)
    {
        if (!string.IsNullOrWhiteSpace(evt.Body))
            return evt.Body!;

        if (evt.AnchorAtUtc is not { } anchorUtc)
            return "Tenés un recordatorio.";

        return IanaTimeZone.TryFindTimeZone(evt.TimeZoneId, out var zone)
            ? $"Es a las {TimeZoneInfo.ConvertTimeFromUtc(anchorUtc, zone):HH:mm}."
            : $"Es a las {anchorUtc:HH:mm} UTC.";
    }
}
