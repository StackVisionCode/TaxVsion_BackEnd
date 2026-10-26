using BuildingBlocks.Messaging.ClientFormsIntegrationEvents;
using BuildingBlocks.Persistence;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Domain.Directory;

namespace TaxVision.Notification.Application.Consumers.ClientForms;

public static class ClientFormNotificationSettingsSyncRequestedConsumer
{
    public static async Task Handle(
        ClientFormNotificationSettingsSyncRequestedIntegrationEvent command,
        IClientFormNotificationSettingsRepository repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct
    )
    {
        var row = await repository.GetAsync(command.TenantId, ct);
        if (row is null)
            await repository.AddAsync(
                ClientFormNotificationSettingsProjection.Create(
                    command.TenantId,
                    command.PushNotificationsEnabled,
                    command.OccurredOn
                ),
                ct
            );
        else
            row.Reconcile(command.PushNotificationsEnabled, command.OccurredOn);
        await unitOfWork.SaveChangesAsync(ct);
    }
}
