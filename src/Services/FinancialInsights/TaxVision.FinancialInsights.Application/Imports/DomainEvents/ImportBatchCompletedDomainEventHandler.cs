using BuildingBlocks.Messaging.FinancialInsightsIntegrationEvents;
using TaxVision.FinancialInsights.Domain.Imports;
using TaxVision.FinancialInsights.Domain.Imports.Events;
using Wolverine;

namespace TaxVision.FinancialInsights.Application.Imports.DomainEvents;

/// <summary>
/// El lote cerró: devuelve el aviso de integración (cascada por el outbox). Corre desde la cola domain-events, después
/// del commit que guardó el cambio de estado; todo sale del evento, no relee el lote.
/// </summary>
public static class ImportBatchCompletedDomainEventHandler
{
    public static FinancialImportBatchCompletedIntegrationEvent Handle(
        ImportBatchCompletedDomainEvent completed,
        Envelope envelope
    ) =>
        new()
        {
            TenantId = completed.TenantId,
            OccurredOn = completed.CompletedAtUtc,
            CorrelationId = envelope.CorrelationId ?? string.Empty,
            BatchId = completed.BatchId,
            CustomerId = completed.CustomerId,
            CreatedByUserId = completed.CreatedBy,
            Source = completed.Source == ImportBatchSource.Portal
                ? FinancialImportBatchSources.Portal
                : FinancialImportBatchSources.Staff,
            CompletedDocuments = completed.Completed,
            NeedsReviewDocuments = completed.NeedsReview,
            FailedDocuments = completed.Failed,
            RejectedDocuments = completed.Rejected,
            CompletedAtUtc = completed.CompletedAtUtc,
        };
}
