using BuildingBlocks.Domain;

namespace TaxVision.FinancialInsights.Domain.Imports.Events;

/// <summary>Todos los documentos del lote llegaron a un estado final en este ciclo. Se levanta una sola vez por ciclo.</summary>
public sealed record ImportBatchCompletedDomainEvent(
    Guid BatchId,
    Guid TenantId,
    Guid CustomerId,
    Guid CreatedBy,
    ImportBatchSource Source,
    int Completed,
    int NeedsReview,
    int Failed,
    int Rejected,
    DateTime CompletedAtUtc
) : IDomainEvent;
