using BuildingBlocks.Authorization;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CampaignsIntegrationEvents;
using Microsoft.Extensions.Logging;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;
using TaxVision.Notification.Domain.Preferences;

namespace TaxVision.Notification.Application.Consumers.Offboarding;

/// <summary>
/// Campaigns pausó los agendados de un empleado retirado sin sucesor. Avisa a quienes tienen
/// <c>campaigns.manage</c> o <c>campaigns.send</c> con la lista de campañas: hay que cancelarlas o volver
/// a agendarlas.
/// </summary>
public static class CampaignSchedulesPausedOnOffboardingConsumer
{
    public const string EmailEventKey = "campaign.schedules_paused_on_offboarding.v1";

    // Más nombres que esto en campana/push se resumen ("and N more"); el correo los lista todos.
    private const int MaxNamesInText = 5;

    public static async Task Handle(
        CampaignSchedulesPausedOnOffboardingIntegrationEvent evt,
        OffboardingNoticeSender sender,
        ICorrelationContext correlation,
        ILogger<CampaignSchedulesPausedOnOffboardingIntegrationEvent> logger,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            if (evt.Schedules is not { Count: > 0 })
            {
                logger.LogWarning(
                    "Paused campaign schedules notice for tenant {TenantId} has no schedules.",
                    evt.TenantId
                );
                return;
            }

            var recipients = await sender.ByPermissionsAsync(
                evt.TenantId,
                [CampaignsPermissions.Manage, CampaignsPermissions.Send],
                ct
            );
            if (recipients.Count == 0)
            {
                logger.LogWarning(
                    "Paused campaign schedules for tenant {TenantId} have no active campaigns.manage/send recipient.",
                    evt.TenantId
                );
                return;
            }

            var names = CampaignNames(evt.Schedules);
            var campaignIds = evt.Schedules.Select(s => s.CampaignId).Distinct().ToArray();
            // El texto habla de campañas (lo que ve la oficina), no de agendados.
            var count = names.Length;
            var link = await sender.StaffLinkAsync(evt.TenantId, "/campaigns", ct);

            await sender.SendAsync(
                new OffboardingNoticeContext(
                    evt.TenantId,
                    evt.EventId,
                    evt.OccurredOn,
                    "campaign.schedules_paused_on_offboarding.v1",
                    NotificationKinds.CampaignsSchedulesPausedOnOffboarding,
                    NotificationCategory.Collaboration,
                    EmailEventKey,
                    link,
                    campaignIds.Length == 1 ? campaignIds[0].ToString() : null,
                    // Un retiro = un aviso, aunque Campaigns lo reintente.
                    $"campaigns.schedules_paused_on_offboarding:{evt.OffboardedUserId:N}",
                    // Mismos agendados pausados = mismo correo; otros agendados (otro retiro) sí avisan.
                    DeliveryKey: $"schedules:{evt.OffboardedUserId:N}:{ScheduleSet(evt.Schedules)}"
                ),
                [
                    new OffboardingNotice(
                        recipients,
                        count == 1 ? "A scheduled campaign was paused" : $"{count} scheduled campaigns were paused",
                        Body(count, names),
                        new Dictionary<string, object?>
                        {
                            ["campaign_count"] = count,
                            ["campaign_names"] = names,
                            ["campaigns_url"] = link,
                        },
                        new Dictionary<string, string>
                        {
                            ["campaignCount"] = count.ToString(),
                            ["scheduleCount"] = evt.Schedules.Count.ToString(),
                        }
                    ),
                ],
                ct
            );
        }
    }

    private static string ScheduleSet(IEnumerable<PausedCampaignSchedule> schedules) =>
        string.Join(",", schedules.Select(s => s.ScheduleId.ToString("N")).Distinct().Order(StringComparer.Ordinal));

    private static string[] CampaignNames(IEnumerable<PausedCampaignSchedule> schedules) =>
        schedules
            .Select(s => string.IsNullOrWhiteSpace(s.CampaignName) ? "Untitled campaign" : s.CampaignName.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string Body(int count, IReadOnlyList<string> names)
    {
        var listed = string.Join(", ", names.Take(MaxNamesInText));
        if (names.Count > MaxNamesInText)
            listed += $" and {names.Count - MaxNamesInText} more";
        return count == 1
            ? $"It was scheduled by someone who's no longer with your office, so it won't send: {listed}. "
                + "Cancel it or schedule it again in Campaigns."
            : $"They were scheduled by someone who's no longer with your office, so they won't send: {listed}. "
                + "Cancel them or schedule them again in Campaigns.";
    }
}
