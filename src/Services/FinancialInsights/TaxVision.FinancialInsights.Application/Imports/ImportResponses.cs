using TaxVision.FinancialInsights.Domain.Imports;

namespace TaxVision.FinancialInsights.Application.Imports;

public sealed record ImportDocumentView(
    Guid Id,
    Guid FileId,
    ImportDocumentStatus Status,
    string? FailureReason,
    DateTime UpdatedAtUtc
)
{
    public static ImportDocumentView From(ImportDocument document) =>
        new(document.Id, document.FileId, document.Status, document.FailureReason, document.UpdatedAtUtc);
}

public sealed record ImportBatchView(
    Guid Id,
    Guid CustomerId,
    Guid CreatedBy,
    DateTime CreatedAtUtc,
    IReadOnlyList<ImportDocumentView> Documents
)
{
    public static ImportBatchView From(ImportBatch batch) =>
        new(
            batch.Id,
            batch.CustomerId,
            batch.CreatedBy,
            batch.CreatedAtUtc,
            batch.Documents.Select(ImportDocumentView.From).ToArray()
        );
}

public sealed record FinancialMovementView(
    Guid Id,
    Guid ImportDocumentId,
    DateOnly TransactionDate,
    string Description,
    decimal Amount,
    string Currency,
    string Direction,
    string SuggestedCategory,
    int SourcePage,
    bool RequiresReview
);

public sealed record ImportBatchDetailView(
    Guid Id,
    Guid CustomerId,
    string? CustomerDisplayName,
    Guid CreatedBy,
    DateTime CreatedAtUtc,
    IReadOnlyList<ImportDocumentView> Documents,
    IReadOnlyList<FinancialMovementView> Movements
);

public sealed record ImportDocumentCountsView(
    int Total,
    int AwaitingScan,
    int Ready,
    int Processing,
    int Completed,
    int NeedsReview,
    int Rejected,
    int Failed
);

public sealed record ImportBatchSummaryView(
    Guid Id,
    Guid CustomerId,
    string? CustomerDisplayName,
    Guid CreatedBy,
    DateTime CreatedAtUtc,
    ImportDocumentCountsView DocumentCounts
);
