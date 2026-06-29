using TaxVision.Customer.Domain.Imports;

namespace TaxVision.Customer.Application.Imports.Dtos;

public sealed record CustomerImportAttemptResponse(
    Guid Id,
    Guid TenantId,
    Guid CreatedByUserId,
    string IdempotencyKey,
    ImportStatus Status,
    DuplicateStrategy Strategy,
    ImportSourceKind SourceKind,
    string SourceFileName,
    int TotalRows,
    int ProcessedRows,
    int SuccessCount,
    int UpdatedCount,
    int SkippedCount,
    int FailedCount,
    DateTime CreatedAtUtc,
    DateTime? StartedAtUtc,
    DateTime? CompletedAtUtc,
    DateTime? CanceledAtUtc,
    Guid? CanceledByUserId,
    string? FailureReason
);
