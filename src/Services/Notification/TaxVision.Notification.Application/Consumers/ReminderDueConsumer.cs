using System.Globalization;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.ReminderIntegrationEvents;
using BuildingBlocks.TimeZones;
using BuildingBlocks.TimeZones.Formatting;
using Microsoft.Extensions.Options;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;
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
/// El gate de <see cref="NotificationCategory.Reminders"/> lo aplican el dispatcher (in-app y push) y
/// el gateway de correo, así que apagar un canal lo apaga sin lógica en este consumer.
/// </para>
/// </summary>
public static class ReminderDueConsumer
{
    private const string TemplateKey = "reminder.due";

    public static async Task Handle(
        ReminderDueIntegrationEvent evt,
        NotificationDispatcher dispatcher,
        IRecipientLocale locales,
        UserEmailResolver emailResolver,
        IOptions<PortalOptions> portal,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            var locale = await locales.ForUserAsync(evt.TenantId, evt.UserId, ct);
            var body = BuildBody(evt, locale);

            // El correo va PRIMERO, y no es un detalle de estilo: es el único de los tres canales que
            // puede lanzar (EnsureRendered, cuando Scribe está caído) y Wolverine reintenta el
            // mensaje ENTERO. Con el correo al final, cada reintento volvería a registrar el in-app y
            // a mandar el push — el usuario vería el mismo recordatorio repetido hasta la DLQ.
            var email = await emailResolver.ResolveAsync(evt.TenantId, evt.UserId, ct);
            if (email is not null)
                await dispatcher.DispatchAsync(
                    Intent(evt, email, correlation) with
                    {
                        Email = new EmailContent(
                            "reminder.due.v1",
                            new Dictionary<string, object?>
                            {
                                ["title"] = evt.Title,
                                ["body"] = body,
                                ["category"] = evt.Category,
                                ["snooze_count"] = evt.SnoozeCount,
                                ["portal_link"] = portal.Value.BaseUrl.TrimEnd('/'),
                            },
                            ScribeRenderAudience.Staff,
                            // Clave de deduplicación del correo; el kind del catálogo va aparte.
                            TemplateKey: TemplateKey
                        ),
                    },
                    ct
                );

            await dispatcher.DispatchAsync(
                Intent(evt, email: null, correlation) with
                {
                    InApp = new InAppContent(
                        Params: new Dictionary<string, string>
                        {
                            ["category"] = evt.Category,
                            ["title"] = evt.Title,
                            ["snoozeCount"] = evt.SnoozeCount.ToString(CultureInfo.InvariantCulture),
                        },
                        ResourceId: evt.ReminderId.ToString(),
                        ResourceContext: BuildResourceContext(evt),
                        TemplateKey: TemplateKey
                    ),
                    Push = new PushContent(),
                },
                ct
            );
        }
    }

    private static NotificationIntent Intent(
        ReminderDueIntegrationEvent evt,
        string? email,
        ICorrelationContext correlation
    ) =>
        new(
            NotificationKinds.RemindersDue,
            evt.TenantId,
            new NotificationTarget(evt.UserId, email),
            evt.EventId,
            "reminder.due.v1",
            evt.OccurredOn,
            correlation.CorrelationId
        );

    private static Dictionary<string, string> BuildResourceContext(ReminderDueIntegrationEvent evt)
    {
        var context = new Dictionary<string, string> { ["sourceType"] = evt.Category };
        if (evt.TargetId is { } targetId)
            context["sourceId"] = targetId.ToString();
        return context;
    }

    /// <summary>
    /// El cuerpo que escribió el usuario si lo hay (tal cual); si no, la hora del ancla <b>en su zona</b> y en su
    /// idioma — mostrarla en UTC sería inútil justo en el aviso donde la hora es todo el contenido.
    /// </summary>
    private static string BuildBody(ReminderDueIntegrationEvent evt, RecipientLocale locale)
    {
        if (!string.IsNullOrWhiteSpace(evt.Body))
            return evt.Body!;

        if (evt.AnchorAtUtc is not { } anchorUtc)
            return EmailText.For("reminder.body.generic", locale);

        // Fecha, hora y zona escrita: el aviso puede llegar días antes y el dueño puede estar en otra zona.
        var date = ZonedTimeFormatter.DateTime(anchorUtc, ResolvedTimeZone.Resolve(evt.TimeZoneId), locale.Language);
        return EmailText.For("reminder.body.scheduled", locale, new Dictionary<string, string> { ["date"] = date });
    }
}
