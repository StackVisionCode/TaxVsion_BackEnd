using BuildingBlocks.Common;
using BuildingBlocks.Messaging.TasksIntegrationEvents;
using TaxVision.Tasks.Domain.Series.Events;

namespace TaxVision.Tasks.Application.Series.DomainEvents;

/// <summary>La serie terminó sola: el aviso a quien la gestiona sale por el outbox.</summary>
public static class TaskSeriesEndedDomainEventHandler
{
    public static TaskSeriesEndedIntegrationEvent Handle(
        TaskSeriesEndedDomainEvent ended,
        ICorrelationContext correlation
    ) =>
        new()
        {
            TenantId = ended.TenantId,
            CorrelationId = correlation.CorrelationId,
            SeriesId = ended.SeriesId,
            Title = ended.Title,
            OwnerUserId = ended.OwnerUserId,
        };
}
