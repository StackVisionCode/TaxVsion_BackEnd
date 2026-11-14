using BuildingBlocks.Domain;
using BuildingBlocks.Results;

namespace TaxVision.FinancialInsights.Domain.Imports;

/// <summary>Documento de un lote. Solo cambia de estado a través de <see cref="ImportBatch"/>.</summary>
public sealed class ImportDocument : ITenantOwned
{
    public const int MaxFailureReasonLength = 128;

    private ImportDocument() { }

    public Guid Id { get; private set; }
    public Guid BatchId { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid FileId { get; private set; }
    public ImportDocumentStatus Status { get; private set; }
    public string? FailureReason { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }

    // Un intento de moverlo de tenant se ignora; la raíz devuelve el error.
    void ITenantOwned.SetTenant(Guid tenantId)
    {
        if (TenantGuard.CanAssign(TenantId, tenantId).IsSuccess)
            TenantId = tenantId;
    }

    internal static ImportDocument Create(Guid batchId, Guid tenantId, Guid fileId) =>
        new()
        {
            Id = Guid.NewGuid(),
            BatchId = batchId,
            TenantId = tenantId,
            FileId = fileId,
            Status = ImportDocumentStatus.AwaitingScan,
            UpdatedAtUtc = DateTime.UtcNow,
        };

    internal Result MarkReady() => Move(ImportDocumentStatus.AwaitingScan, ImportDocumentStatus.Ready, null);

    internal Result Reject(string reason) =>
        MoveWithReason(ImportDocumentStatus.AwaitingScan, ImportDocumentStatus.Rejected, reason);

    internal Result StartProcessing() => Move(ImportDocumentStatus.Ready, ImportDocumentStatus.Processing, null);

    internal Result ReturnToQueue() => Move(ImportDocumentStatus.Processing, ImportDocumentStatus.Ready, null);

    internal Result Complete(bool needsReview) =>
        Move(
            ImportDocumentStatus.Processing,
            needsReview ? ImportDocumentStatus.NeedsReview : ImportDocumentStatus.Completed,
            null
        );

    internal Result Fail(string reason) =>
        MoveWithReason(ImportDocumentStatus.Processing, ImportDocumentStatus.Failed, reason);

    internal Result HoldForReview(string reason) =>
        MoveWithReason(ImportDocumentStatus.Processing, ImportDocumentStatus.NeedsReview, reason);

    internal Result RetryBeforeInference(bool inferenceAttempted)
    {
        if (Status != ImportDocumentStatus.Failed)
            return Result.Failure(FinancialInsightsErrors.NotRetryable);
        // Reintentar después de llamar al modelo se volvería a cobrar.
        if (inferenceAttempted)
            return Result.Failure(FinancialInsightsErrors.InferenceAlreadyAttempted);
        return Move(ImportDocumentStatus.Failed, ImportDocumentStatus.Ready, null);
    }

    private Result MoveWithReason(ImportDocumentStatus from, ImportDocumentStatus to, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            return Result.Failure(FinancialInsightsErrors.FailureReasonRequired);
        var trimmed = reason.Trim();
        return Move(from, to, trimmed.Length > MaxFailureReasonLength ? trimmed[..MaxFailureReasonLength] : trimmed);
    }

    private Result Move(ImportDocumentStatus from, ImportDocumentStatus to, string? reason)
    {
        if (Status != from)
            return Result.Failure(FinancialInsightsErrors.InvalidTransition);
        Status = to;
        FailureReason = reason;
        UpdatedAtUtc = DateTime.UtcNow;
        return Result.Success();
    }
}
