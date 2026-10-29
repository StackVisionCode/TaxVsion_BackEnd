using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using TaxVision.Tasks.ClientForms.Domain.Settings;

namespace TaxVision.Tasks.ClientForms.Application.Settings;

/// <summary>
/// Backfill de <c>PushNotificationsEnabled</c> hacia la proyección de Notification: el flag existía en
/// Tasks antes de que Notification lo proyectara. El comando lleva el <c>OccurredOn</c> más viejo posible,
/// así Notification solo lo aplica donde no hay fila: cualquier cambio real (con su fecha) gana siempre,
/// llegue antes o después. Repetirlo en cada arranque no cambia nada.
/// </summary>
public static class ClientFormNotificationSettingsBackfill
{
    public static readonly DateTime OccurredOnUtc = DateTime.UnixEpoch;

    public static IReadOnlyList<ClientFormNotificationSettingsSyncRequestedIntegrationEvent> Commands(
        IEnumerable<FormModuleSettings> settings,
        string correlationId
    ) =>
        settings
            .Select(row =>
            {
                var commandId = Guid.NewGuid();
                return new ClientFormNotificationSettingsSyncRequestedIntegrationEvent
                {
                    EventId = commandId,
                    CommandId = commandId,
                    TenantId = row.TenantId,
                    CorrelationId = correlationId,
                    OccurredOn = OccurredOnUtc,
                    PushNotificationsEnabled = row.PushNotificationsEnabled,
                };
            })
            .ToList();
}
