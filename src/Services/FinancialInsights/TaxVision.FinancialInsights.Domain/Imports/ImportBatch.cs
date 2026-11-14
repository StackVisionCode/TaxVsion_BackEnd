using BuildingBlocks.Domain;

namespace TaxVision.FinancialInsights.Domain.Imports;

public enum ImportDocumentStatus { AwaitingScan, Ready, Processing, Completed, NeedsReview, Rejected, Failed }

public sealed class ImportBatch : ITenantOwned
{
    private ImportBatch() { }
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid CustomerId { get; private set; }
    public Guid CreatedBy { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public List<ImportDocument> Documents { get; private set; } = [];
    void ITenantOwned.SetTenant(Guid tenantId)
    {
        if (TenantId != Guid.Empty && TenantId != tenantId) throw new InvalidOperationException("Tenant cannot change.");
        TenantId = tenantId;
    }

    public static ImportBatch Create(Guid tenantId, Guid customerId, Guid createdBy, IEnumerable<Guid> fileIds)
    {
        if (tenantId == Guid.Empty || customerId == Guid.Empty || createdBy == Guid.Empty)
            throw new ArgumentException("Tenant, customer and creator are required.");
        var ids = fileIds.ToArray();
        if (ids.Length is < 1 or > 20 || ids.Any(id => id == Guid.Empty) || ids.Distinct().Count() != ids.Length)
            throw new ArgumentException("Provide 1 to 20 distinct file IDs.", nameof(fileIds));
        var batch = new ImportBatch { Id = Guid.NewGuid(), TenantId = tenantId, CustomerId = customerId, CreatedBy = createdBy, CreatedAtUtc = DateTime.UtcNow };
        batch.Documents.AddRange(ids.Select(id => ImportDocument.Create(batch.Id, tenantId, id)));
        return batch;
    }
}

public sealed class ImportDocument : ITenantOwned
{
    private ImportDocument() { }
    public Guid Id { get; private set; }
    public Guid BatchId { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid FileId { get; private set; }
    public ImportDocumentStatus Status { get; private set; }
    public string? FailureReason { get; private set; }
    public DateTime UpdatedAtUtc { get; private set; }
    void ITenantOwned.SetTenant(Guid tenantId)
    {
        if (TenantId != Guid.Empty && TenantId != tenantId) throw new InvalidOperationException("Tenant cannot change.");
        TenantId = tenantId;
    }

    internal static ImportDocument Create(Guid batchId, Guid tenantId, Guid fileId) =>
        new() { Id = Guid.NewGuid(), BatchId = batchId, TenantId = tenantId, FileId = fileId, Status = ImportDocumentStatus.AwaitingScan, UpdatedAtUtc = DateTime.UtcNow };

    public bool MarkReady()
    {
        if (Status != ImportDocumentStatus.AwaitingScan) return false;
        Status = ImportDocumentStatus.Ready;
        UpdatedAtUtc = DateTime.UtcNow;
        return true;
    }

    public bool Reject(string reason)
    {
        if (Status != ImportDocumentStatus.AwaitingScan) return false;
        Status = ImportDocumentStatus.Rejected;
        FailureReason = reason;
        UpdatedAtUtc = DateTime.UtcNow;
        return true;
    }

    public bool Complete(bool needsReview)
    {
        if (Status != ImportDocumentStatus.Processing) return false;
        Status = needsReview ? ImportDocumentStatus.NeedsReview : ImportDocumentStatus.Completed;
        FailureReason = null;
        UpdatedAtUtc = DateTime.UtcNow;
        return true;
    }

    public bool Fail(string reason)
    {
        if (Status != ImportDocumentStatus.Processing) return false;
        Status = ImportDocumentStatus.Failed;
        FailureReason = reason;
        UpdatedAtUtc = DateTime.UtcNow;
        return true;
    }

    public bool HoldForReview(string reason)
    {
        if (Status != ImportDocumentStatus.Processing) return false;
        Status = ImportDocumentStatus.NeedsReview;
        FailureReason = reason;
        UpdatedAtUtc = DateTime.UtcNow;
        return true;
    }

    public bool RetryBeforeInference()
    {
        if (Status != ImportDocumentStatus.Failed) return false;
        Status = ImportDocumentStatus.Ready;
        FailureReason = null;
        UpdatedAtUtc = DateTime.UtcNow;
        return true;
    }
}
