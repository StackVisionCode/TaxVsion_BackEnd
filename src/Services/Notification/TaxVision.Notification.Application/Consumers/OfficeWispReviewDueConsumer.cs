using System.Globalization;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.TenantIntegrationEvents;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;
using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Domain.Preferences;

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
    private const NotificationCategory Category = NotificationCategory.AccountSecurity;
    private static readonly CultureInfo English = CultureInfo.GetCultureInfo("en-US");

    public static async Task Handle(
        OfficeWispReviewDueIntegrationEvent evt,
        NotificationDispatcher dispatcher,
        IRecipientResolver resolver,
        IUserEmailResolver emails,
        IUserNotificationPreferenceRepository preferences,
        IEmailDispatchGateway gateway,
        IScribeRenderClient scribe,
        ITenantHostResolver hosts,
        IOptions<PortalOptions> portal,
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
                emails,
                preferences,
                gateway,
                scribe,
                correlation,
                ct
            );

            foreach (var userId in recipients)
            {
                await RecordInAppAsync(evt, notice, userId, dispatcher, correlation, ct);
                await dispatcher.SendPushAsync(
                    new PushDispatchRequest(
                        evt.OfficeTenantId,
                        userId,
                        notice.Title,
                        notice.Body,
                        Category,
                        NotificationKinds.TenantWispReviewDue,
                        NotificationKinds.TenantWispReviewDue,
                        evt.EventId,
                        correlation.CorrelationId,
                        Data: new Dictionary<string, string>
                        {
                            ["kind"] = NotificationKinds.TenantWispReviewDue,
                            ["resourceType"] = NotificationContractNames.For(
                                NotificationResourceType.ComplianceSettings
                            ),
                            ["resourceId"] = evt.OfficeTenantId.ToString(),
                        },
                        Link: settingsUrl,
                        SkipWhenNoDevices: true
                    ),
                    ct
                );
            }
        }
    }

    private static async Task SendEmailsAsync(
        OfficeWispReviewDueIntegrationEvent evt,
        WispNotice notice,
        string settingsUrl,
        IReadOnlyList<Guid> recipients,
        IUserEmailResolver emails,
        IUserNotificationPreferenceRepository preferences,
        IEmailDispatchGateway gateway,
        IScribeRenderClient scribe,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        // El gate de preferencias del dispatcher no cubre el correo: se filtra acá (lo obligatorio no se filtra).
        var skipPreference =
            NotificationCategoryRules.IsLocked(Category)
            || NotificationCatalog
                .GetRequired(NotificationKinds.TenantWispReviewDue)
                .IsMandatoryFor(NotificationChannel.Email);
        var addresses = new List<string>();
        foreach (var userId in recipients)
        {
            if (
                !skipPreference
                && !await preferences.IsEnabledAsync(
                    evt.OfficeTenantId,
                    userId,
                    Category,
                    NotificationChannel.Email,
                    ct
                )
            )
                continue;
            var address = await emails.ResolveAsync(evt.OfficeTenantId, userId, ct);
            if (!string.IsNullOrWhiteSpace(address) && !addresses.Contains(address, StringComparer.OrdinalIgnoreCase))
                addresses.Add(address);
        }
        if (addresses.Count == 0)
            return;

        var render = (
            await scribe.RenderAsync(
                EmailEventKey,
                evt.OfficeTenantId,
                new Dictionary<string, object?>
                {
                    ["is_overdue"] = notice.IsOverdue,
                    ["due_date"] = notice.DueDate,
                    ["settings_url"] = settingsUrl,
                },
                ct
            )
        ).EnsureRendered(EmailEventKey);

        foreach (var address in addresses)
            await gateway.QueueEmailAsync(
                render.ToDispatchRequest(
                    evt.OfficeTenantId,
                    address,
                    EmailEventKey,
                    evt.EventId,
                    correlation.CorrelationId
                ),
                ct
            );
    }

    private static Task RecordInAppAsync(
        OfficeWispReviewDueIntegrationEvent evt,
        WispNotice notice,
        Guid userId,
        NotificationDispatcher dispatcher,
        ICorrelationContext correlation,
        CancellationToken ct
    ) =>
        dispatcher.RecordInAppAsync(
            evt.OfficeTenantId,
            $"user:{userId:N}",
            notice.Title,
            Category,
            NotificationKinds.TenantWispReviewDue,
            evt.EventId,
            correlation.CorrelationId,
            recipientUserId: userId,
            inApp: new InAppNotificationRequest(
                NotificationKinds.TenantWispReviewDue,
                userId,
                evt.EventId,
                "tenant.office_wisp_review_due.v1",
                evt.OccurredOn,
                notice.Title,
                FallbackBody: notice.Body,
                ResourceId: evt.OfficeTenantId.ToString(),
                // El front abre /company/settings en la sección indicada.
                ResourceContext: new Dictionary<string, string> { ["section"] = "compliance" },
                Params: new Dictionary<string, string>
                {
                    ["dueOn"] = evt.DueOn.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                    ["dueDate"] = notice.DueDate,
                    ["stage"] = evt.Stage,
                },
                // Un aviso sin leer de la misma oficina, vencimiento y etapa se actualiza en vez de duplicarse.
                GroupKey: $"tenant.wisp_review_due:{evt.OfficeTenantId:N}:{evt.DueOn:yyyyMMdd}:{evt.Stage}"
            ),
            ct: ct
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
