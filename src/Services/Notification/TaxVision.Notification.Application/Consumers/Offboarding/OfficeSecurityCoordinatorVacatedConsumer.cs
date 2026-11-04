using BuildingBlocks.Common;
using BuildingBlocks.Messaging.TenantIntegrationEvents;
using Microsoft.Extensions.Logging;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;
using TaxVision.Notification.Domain.Preferences;

namespace TaxVision.Notification.Application.Consumers.Offboarding;

/// <summary>
/// Tenant vació al responsable de seguridad (WISP) porque fue retirado. Avisa a quienes tienen
/// <c>settings.manage</c> (correo, campana y push) para que designen a otro en Office settings → Compliance.
/// </summary>
public static class OfficeSecurityCoordinatorVacatedConsumer
{
    public const string EmailEventKey = "tenant.security_coordinator_vacated.v1";
    public const string Title = "Your office needs a new security coordinator";
    public const string Body =
        "The person designated as your security coordinator is no longer with your office, so the role is now vacant. "
        + "The FTC Safeguards Rule requires one. Choose a new coordinator in Office settings → Compliance.";

    public static async Task Handle(
        OfficeSecurityCoordinatorVacatedIntegrationEvent evt,
        OffboardingNoticeSender sender,
        ICorrelationContext correlation,
        ILogger<OfficeSecurityCoordinatorVacatedIntegrationEvent> logger,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            if (evt.Reason != OfficeSecurityCoordinatorVacancyReasons.Offboarded)
            {
                logger.LogWarning(
                    "Security coordinator vacancy notice skipped for tenant {TenantId}: unknown reason {Reason}.",
                    evt.OfficeTenantId,
                    evt.Reason
                );
                return;
            }

            var recipients = await sender.ByPermissionsAsync(
                evt.OfficeTenantId,
                [OfficeComplianceRecipients.SettingsManagePermission],
                ct
            );
            if (recipients.Count == 0)
            {
                logger.LogWarning(
                    "Security coordinator vacancy for tenant {TenantId} has no active settings.manage recipient.",
                    evt.OfficeTenantId
                );
                return;
            }

            var link = await sender.StaffLinkAsync(evt.OfficeTenantId, "/company/settings#compliance", ct);
            await sender.SendAsync(
                new OffboardingNoticeContext(
                    evt.OfficeTenantId,
                    evt.EventId,
                    evt.OccurredOn,
                    "tenant.office_security_coordinator_vacated.v1",
                    NotificationKinds.TenantSecurityCoordinatorVacated,
                    NotificationCategory.AccountSecurity,
                    EmailEventKey,
                    link,
                    evt.OfficeTenantId.ToString(),
                    // Una sola fila sin leer por oficina aunque se repita.
                    $"tenant.security_coordinator_vacated:{evt.OfficeTenantId:N}",
                    // El front abre /company/settings en la sección indicada.
                    new Dictionary<string, string> { ["section"] = "compliance" }
                ),
                [
                    new OffboardingNotice(
                        recipients,
                        Title,
                        Body,
                        new Dictionary<string, object?> { ["settings_url"] = link }
                    ),
                ],
                ct
            );
        }
    }
}
