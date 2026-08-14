using BuildingBlocks.Messaging.CalendarIntegrationEvents;
using Microsoft.Extensions.Logging;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Domain.Notifications;
using TaxVision.Notification.Domain.Preferences;

namespace TaxVision.Notification.Application.Consumers.Calendar;

/// <summary>
/// Deja pasar sólo a quien quiere recibir el correo.
///
/// <para>
/// Un asistente sin <c>UserId</c> —un cliente invitado— no tiene preferencia que consultar y recibe
/// igual: es el mismo criterio que ya usa el dispatcher para el in-app de un invitado sin cuenta.
/// </para>
/// </summary>
internal static class CalendarRecipients
{
    public static async Task<IReadOnlyList<string>> AllowedAsync(
        IReadOnlyList<AppointmentRecipient> recipients,
        Guid tenantId,
        IUserNotificationPreferenceRepository preferences,
        ILogger logger,
        string templateKey,
        CancellationToken ct
    )
    {
        var allowed = new List<string>(recipients.Count);
        foreach (var recipient in recipients)
        {
            if (recipient.UserId is not { } userId)
            {
                allowed.Add(recipient.Email);
                continue;
            }

            var enabled = await preferences.IsEnabledAsync(
                tenantId,
                userId,
                NotificationCategory.Calendar,
                NotificationChannel.Email,
                ct
            );

            if (enabled)
            {
                allowed.Add(recipient.Email);
                continue;
            }

            logger.LogInformation(
                "Calendar email suppressed by user preference: tenant {TenantId}, user {UserId}, template {TemplateKey}.",
                tenantId,
                userId,
                templateKey
            );
        }

        return allowed;
    }
}
