using BuildingBlocks.Common;
using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using TaxVision.Notification.Application.Common;

namespace TaxVision.Notification.Application.Consumers;

public static class FileInfectedDetectedConsumer
{
    public static async Task Handle(
        FileInfectedDetectedIntegrationEvent evt,
        NotificationDispatcher dispatcher,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            await dispatcher.RecordInAppAsync(
                evt.TenantId,
                "role:TenantAdmin",
                $"Archivo malicioso bloqueado ({evt.FileId:N})",
                "cloudstorage.file_infected",
                evt.EventId,
                correlation.CorrelationId,
                ct
            );
        }
    }
}

public static class StorageLimitExceededConsumer
{
    public static async Task Handle(
        StorageLimitExceededIntegrationEvent evt,
        NotificationDispatcher dispatcher,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (correlation.Push(Correlation.From(evt.CorrelationId, evt.EventId)))
        {
            await dispatcher.RecordInAppAsync(
                evt.TenantId,
                "role:TenantAdmin",
                "Límite de almacenamiento alcanzado",
                "cloudstorage.quota_exceeded",
                evt.EventId,
                correlation.CorrelationId,
                ct
            );
        }
    }
}
