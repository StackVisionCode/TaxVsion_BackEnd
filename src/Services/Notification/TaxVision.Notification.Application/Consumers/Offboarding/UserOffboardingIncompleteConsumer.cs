using BuildingBlocks.Authorization;
using BuildingBlocks.Common;
using BuildingBlocks.Messaging.AuthIntegrationEvents;
using Microsoft.Extensions.Logging;
using TaxVision.Notification.Application.Common;
using TaxVision.Notification.Domain.Catalog;
using TaxVision.Notification.Domain.Preferences;

namespace TaxVision.Notification.Application.Consumers.Offboarding;

/// <summary>
/// Auth dio por incompleto el retiro de un empleado: alguna parte de la plataforma no confirmó su paso en el
/// plazo. Avisa a quienes gestionan usuarios (<c>users.manage</c>) con las partes pendientes en palabras de la
/// oficina (el módulo, nunca el nombre del servicio o del consumer).
/// </summary>
public static class UserOffboardingIncompleteConsumer
{
    public const string EmailEventKey = "auth.user_offboarding_incomplete.v1";

    public static async Task Handle(
        UserOffboardingIncompleteIntegrationEvent evt,
        OffboardingNoticeSender sender,
        ICorrelationContext correlation,
        ILogger<UserOffboardingIncompleteIntegrationEvent> logger,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            var modules = Modules(evt.PendingSteps);
            if (modules.Length == 0)
            {
                logger.LogWarning(
                    "Incomplete offboarding notice for user {UserId} has no pending steps; skipped.",
                    evt.UserId
                );
                return;
            }

            var recipients = await sender.ByPermissionsAsync(evt.TenantId, [UserManagementPermissions.UsersManage], ct);
            if (recipients.Count == 0)
            {
                logger.LogWarning(
                    "Incomplete offboarding of user {UserId} in tenant {TenantId} has no active users.manage recipient.",
                    evt.UserId,
                    evt.TenantId
                );
                return;
            }

            var name = string.IsNullOrWhiteSpace(evt.UserDisplayName)
                ? "A former team member"
                : evt.UserDisplayName.Trim();
            // Abre el avance del handover de esa persona (un id, no datos personales).
            var link = await sender.StaffLinkAsync(evt.TenantId, $"/company/users?handover={evt.UserId}", ct);
            await sender.SendAsync(
                new OffboardingNoticeContext(
                    evt.TenantId,
                    evt.EventId,
                    evt.OccurredOn,
                    "auth.user_offboarding_incomplete.v1",
                    NotificationKinds.AuthOffboardingIncomplete,
                    NotificationCategory.AccountSecurity,
                    EmailEventKey,
                    link,
                    evt.UserId.ToString(),
                    $"auth.offboarding_incomplete:{evt.UserId:N}",
                    new Dictionary<string, string> { ["userId"] = evt.UserId.ToString() },
                    // Un retiro = un aviso, aunque Auth lo vuelva a publicar.
                    DeliveryKey: $"offboarding:{evt.UserId:N}"
                ),
                [
                    new OffboardingNotice(
                        recipients,
                        $"{name}'s offboarding didn't finish",
                        Body(name, modules),
                        new Dictionary<string, object?>
                        {
                            ["user_name"] = name,
                            ["pending_modules"] = modules,
                            ["users_url"] = link,
                        },
                        new Dictionary<string, string>
                        {
                            ["userName"] = name,
                            ["pendingModules"] = string.Join(", ", modules),
                            ["completedSteps"] = evt.CompletedSteps.ToString(),
                            ["expectedSteps"] = evt.ExpectedSteps.ToString(),
                        }
                    ),
                ],
                ct
            );
        }
    }

    private static string[] Modules(IEnumerable<PendingOffboardingStep>? steps) =>
        (steps ?? [])
            .Select(s => string.IsNullOrWhiteSpace(s.Module) ? "Another area" : s.Module.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string Body(string name, IReadOnlyList<string> modules) =>
        $"{name} was offboarded, but {(modules.Count == 1 ? "this area hasn't" : "these areas haven't")} "
        + $"confirmed the handover yet: {string.Join(", ", modules)}. Some of their work or access there may "
        + "still be active. Review it, and contact support if it doesn't clear up.";
}
