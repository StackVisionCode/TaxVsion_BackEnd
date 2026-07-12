using BuildingBlocks.Common;
using BuildingBlocks.Messaging.EmailIntegrationEvents;
using TaxVision.Notification.Application.Abstractions;
using TaxVision.Notification.Domain.Emailing.Accounts;

namespace TaxVision.Notification.Application.Consumers;

/// <summary>Ejecuta una sincronización completa de una cuenta (fuera del request).</summary>
public static class EmailFullSyncRequestedConsumer
{
    public static async Task Handle(
        EmailFullSyncRequestedIntegrationEvent evt,
        IEmailSyncService sync,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (
            correlation.Push(
                string.IsNullOrWhiteSpace(evt.CorrelationId) ? evt.EventId.ToString("N") : evt.CorrelationId
            )
        )
            await sync.SyncAccountAsync(evt.AccountId, SyncType.Full, ct);
    }
}

/// <summary>Ejecuta una sincronización incremental de una cuenta (fuera del request).</summary>
public static class EmailIncrementalSyncRequestedConsumer
{
    public static async Task Handle(
        EmailIncrementalSyncRequestedIntegrationEvent evt,
        IEmailSyncService sync,
        ICorrelationContext correlation,
        CancellationToken ct
    )
    {
        using (
            correlation.Push(
                string.IsNullOrWhiteSpace(evt.CorrelationId) ? evt.EventId.ToString("N") : evt.CorrelationId
            )
        )
            await sync.SyncAccountAsync(evt.AccountId, SyncType.Incremental, ct);
    }
}
