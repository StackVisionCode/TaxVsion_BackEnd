using TaxVision.FinancialInsights.Domain.Imports;

namespace TaxVision.FinancialInsights.Application.Imports;

public sealed record CreateImportBatchRequest(Guid CustomerId, IReadOnlyList<Guid> FileIds);
public sealed record ImportDocumentResponse(Guid FileId, ImportDocumentStatus Status, string? FailureReason);
public sealed record ImportBatchResponse(Guid Id, Guid CustomerId, DateTime CreatedAtUtc, IReadOnlyList<ImportDocumentResponse> Documents)
{
    public static ImportBatchResponse From(ImportBatch batch) => new(batch.Id, batch.CustomerId, batch.CreatedAtUtc,
        batch.Documents.Select(d => new ImportDocumentResponse(d.FileId, d.Status, d.FailureReason)).ToArray());
}
