using System.Globalization;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.TenantIntegrationEvents;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;

namespace TaxVision.Notification.Application.Consumers;

/// <summary>
/// La revisión anual del WISP de la oficina está por vencer o venció (Tenant, una vez por etapa).
/// Avisa al responsable de seguridad y a quienes tienen <c>settings.manage</c>: correo (plantilla de
/// sistema en Scribe, envío por Postmaster), campana y push, con destino Office settings → Compliance.
/// El correo se renderiza una sola vez y antes que nada: es lo único que puede lanzar (Scribe caído), y
/// así un reintento de Wolverine no repite campana ni push. Correo y push dedupan por evento y destinatario.
/// </summary>
public static class OfficeWispReviewDueConsumer
{
    public const string EmailEventKey = "tenant.wisp_review_due.v1";
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    public static async Task Handle(
        OfficeWispReviewDueIntegrationEvent evt,
        NotificationDispatcher dispatcher,
        IRecipientResolver resolver,
        IUserEmailResolver emails,
        IScribeRenderClient scribe,
        ITenantHostResolver hosts,
        IOptions<PortalOptions> portal,
        IRecipientLocale locales,
        ICorrelationContext correlation,
        ILogger<OfficeWispReviewDueIntegrationEvent> logger,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            var isOverdue = evt.Stage == OfficeWispReviewStages.Overdue;
            if (!isOverdue && evt.Stage != OfficeWispReviewStages.DueSoon)
            {
                logger.LogWarning(
                    "WISP review notice skipped for tenant {TenantId}: unknown stage {Stage}.",
                    evt.OfficeTenantId,
                    evt.Stage
                );
                return;
            }

            var recipients = await OfficeComplianceRecipients.ResolveAsync(
                evt.OfficeTenantId,
                evt.SecurityCoordinatorUserId,
                resolver,
                ct
            );
            if (recipients.Count == 0)
            {
                logger.LogWarning(
                    "WISP review notice for tenant {TenantId} has no active recipient (coordinator or settings.manage).",
                    evt.OfficeTenantId
                );
                return;
            }

            var notice = WispNotice.For(evt, isOverdue);
            var host = await hosts.ResolveHostAsync(evt.OfficeTenantId, ct);
            var settingsUrl = $"{TenantEmailLinks.StaffBase(host, portal.Value)}/company/settings#compliance";

            await SendEmailsAsync(
                evt,
                notice,
                settingsUrl,
                recipients,
                dispatcher,
                emails,
                scribe,
                locales,
                correlation,
                ct
            );

            foreach (var userId in recipients)
                await NotifyAsync(evt, notice, settingsUrl, userId, dispatcher, correlation, ct);
        }
    }

    private static async Task SendEmailsAsync(
        OfficeWispReviewDueIntegrationEvent evt,
        WispNotice notice,
        string settingsUrl,
        IReadOnlyList<Guid> recipients,
        NotificationDispatcher dispatcher,
        IUserEmailResolver emails,
        IScribeRenderClient scribe,
        IRecipientLocale locales,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        var addresses = new List<(Guid UserId, string Address)>();
        foreach (var userId in recipients)
        {
            var address = await emails.ResolveAsync(evt.OfficeTenantId, userId, ct);
            if (
                !string.IsNullOrWhiteSpace(address)
                && !addresses.Exists(a => string.Equals(a.Address, address, StringComparison.OrdinalIgnoreCase))
            )
                addresses.Add((userId, address));
        }
        if (addresses.Count == 0)
            return;

        // Un render por idioma y lector; todos antes de encolar, así un fallo de Scribe no deja correos a medias.
        var renders = new RecipientRenders(
            scribe,
            EmailEventKey,
            evt.OfficeTenantId,
            ScribeRenderAudience.Staff,
            new Dictionary<string, object?>
            {
                ["is_overdue"] = notice.IsOverdue,
                // Fecha tipada: Scribe la imprime en el idioma de quien lee.
                ["due_date"] = ScribeDates.Date(evt.DueOn),
                ["settings_url"] = settingsUrl,
            }
        );
        var languages = new RecipientLocaleCache(locales, evt.OfficeTenantId);
        var targets = new List<NotificationTarget>(addresses.Count);
        foreach (var (userId, address) in addresses)
        {
            var locale = await languages.ForUserAsync(userId, ct);
            await renders.ForAsync(locale, ct);
            targets.Add(new NotificationTarget(userId, address, Locale: locale));
        }

        foreach (var target in targets)
            await dispatcher.DispatchAsync(
                Intent(evt, target, correlation) with
                {
                    Email = new EmailContent(
                        EmailEventKey,
                        new Dictionary<string, object?>(),
                        Render: renders.ForAsync
                    ),
                },
                ct
            );
    }

    // Campana y push; el push conserva su enlace directo a Compliance.
    private static Task NotifyAsync(
        OfficeWispReviewDueIntegrationEvent evt,
        WispNotice notice,
        string settingsUrl,
        Guid userId,
        NotificationDispatcher dispatcher,
        ICorrelationContext correlation,
        CancellationToken ct
    ) =>
        dispatcher.DispatchAsync(
            Intent(evt, new NotificationTarget(userId), correlation) with
            {
                InApp = new InAppContent(
                    Params: new Dictionary<string, string>
                    {
                        ["dueOn"] = evt.DueOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        ["dueDate"] = notice.DueDate,
                        ["stage"] = evt.Stage,
                    },
                    ResourceId: evt.OfficeTenantId.ToString(),
                    // El front abre /company/settings en la sección indicada.
                    ResourceContext: new Dictionary<string, string> { ["section"] = "compliance" },
                    // Un aviso sin leer de la misma oficina, vencimiento y etapa se actualiza en vez de duplicarse.
                    GroupKey: $"tenant.wisp_review_due:{evt.OfficeTenantId:N}:{evt.DueOn:yyyyMMdd}:{evt.Stage}",
                    FallbackTitle: notice.Title,
                    FallbackBody: notice.Body
                ),
                Push = new PushContent(
                    SkipWhenNoDevices: true,
                    Title: notice.Title,
                    Body: notice.Body,
                    Link: settingsUrl
                ),
            },
            ct
        );

    private static NotificationIntent Intent(
        OfficeWispReviewDueIntegrationEvent evt,
        NotificationTarget target,
        ICorrelationContext correlation
    ) =>
        new(
            NotificationKinds.TenantWispReviewDue,
            evt.OfficeTenantId,
            target,
            evt.EventId,
            "tenant.office_wisp_review_due.v1",
            evt.OccurredOn,
            correlation.CorrelationId
        );

    private sealed record WispNotice(bool IsOverdue, string DueDate, string Title, string Body)
    {
        private const string Rule =
            "The FTC Safeguards Rule requires reviewing your written information security plan (WISP) at least once a year.";
        private const string HowTo = "Mark it reviewed in Office settings → Compliance.";

        public static WispNotice For(OfficeWispReviewDueIntegrationEvent evt, bool isOverdue)
        {
            var dueDate = evt.DueOn.ToString("MMMM d, yyyy", English);
            return isOverdue
                ? new(true, dueDate, "Your security plan review is overdue", $"It was due {dueDate}. {Rule} {HowTo}")
                : new(false, dueDate, $"Your security plan review is due {dueDate}", $"{Rule} {HowTo}");
        }
    }
}
