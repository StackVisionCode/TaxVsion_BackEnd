using BuildingBlocks.Persistence;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Domain.Notifications;

namespace TaxVision.Notification.Tests.TestDoubles;

/// <summary>Filas de auditoría en memoria para los consumers que corrigen su estado; cuenta los guardados.</summary>
internal sealed class MemoryLogStatusRepository : INotificationLogStatusRepository, IUnitOfWork
{
    public List<NotificationLog> Logs { get; } = [];

    public int Saves { get; private set; }

    public Task<NotificationLog?> FindAsync(Guid tenantId, Guid logId, CancellationToken ct = default) =>
        Task.FromResult(Logs.FirstOrDefault(log => log.TenantId == tenantId && log.Id == logId));

    public Task<NotificationLog?> FindByRelatedEventAsync(
        Guid tenantId,
        Guid relatedEventId,
        string kind,
        NotificationChannel channel,
        string recipient,
        CancellationToken ct = default
    ) =>
        Task.FromResult(
            Logs.FirstOrDefault(log =>
                log.TenantId == tenantId
                && log.RelatedEventId == relatedEventId
                && log.Kind == kind
                && log.Channel == channel
                && log.Recipient == recipient
            )
        );

    public Task<int> SaveChangesAsync(CancellationToken ct = default)
    {
        Saves++;
        return Task.FromResult(0);
    }
}
