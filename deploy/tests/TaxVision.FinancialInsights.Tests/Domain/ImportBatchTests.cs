using BuildingBlocks.Domain;
using TaxVision.FinancialInsights.Domain.Imports;

namespace TaxVision.FinancialInsights.Tests.Domain;

public sealed class ImportBatchTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Customer = Guid.NewGuid();
    private static readonly Guid User = Guid.NewGuid();

    [Fact]
    public void Create_rejects_duplicate_empty_and_oversized_file_lists_without_throwing()
    {
        var file = Guid.NewGuid();

        Assert.Equal("Import.InvalidBatch", ImportBatch.Create(Tenant, Customer, User, [file, file]).Error.Code);
        Assert.Equal("Import.InvalidBatch", ImportBatch.Create(Tenant, Customer, User, []).Error.Code);
        Assert.Equal("Import.InvalidBatch", ImportBatch.Create(Tenant, Customer, User, null).Error.Code);
        Assert.Equal("Import.InvalidBatch", ImportBatch.Create(Tenant, Customer, User, [Guid.Empty]).Error.Code);
        Assert.Equal(
            "Import.InvalidBatch",
            ImportBatch.Create(Tenant, Customer, User, Enumerable.Range(0, 21).Select(_ => Guid.NewGuid()).ToArray()).Error.Code
        );
    }

    [Fact]
    public void Create_requires_tenant_customer_and_creator()
    {
        var files = new[] { Guid.NewGuid() };

        Assert.Equal("Import.TenantRequired", ImportBatch.Create(Guid.Empty, Customer, User, files).Error.Code);
        Assert.Equal("Import.CustomerRequired", ImportBatch.Create(Tenant, Guid.Empty, User, files).Error.Code);
        Assert.Equal("Import.CreatorRequired", ImportBatch.Create(Tenant, Customer, Guid.Empty, files).Error.Code);
    }

    [Fact]
    public void Create_registers_twenty_files_awaiting_scan_in_the_same_tenant()
    {
        var files = Enumerable.Range(0, 20).Select(_ => Guid.NewGuid()).ToArray();

        var batch = ImportBatch.Create(Tenant, Customer, User, files).Value;

        Assert.Equal(Tenant, batch.TenantId);
        Assert.Equal(20, batch.Documents.Count);
        Assert.All(batch.Documents, d => Assert.Equal(ImportDocumentStatus.AwaitingScan, d.Status));
        Assert.All(batch.Documents, d => Assert.Equal(Tenant, d.TenantId));
        Assert.All(batch.Documents, d => Assert.Equal(batch.Id, d.BatchId));
    }

    [Fact]
    public void Tenant_cannot_move_once_assigned()
    {
        var batch = NewBatch(out _);
        var other = Guid.NewGuid();

        Assert.Equal("Import.TenantImmutable", batch.SetTenant(other).Error.Code);
        ((ITenantOwned)batch).SetTenant(other);
        ((ITenantOwned)batch.Documents[0]).SetTenant(other);

        Assert.Equal(Tenant, batch.TenantId);
        Assert.Equal(Tenant, batch.Documents[0].TenantId);
        Assert.True(batch.SetTenant(Tenant).IsSuccess);
    }

    [Fact]
    public void Scan_transitions_are_explicit_and_idempotent()
    {
        var batch = NewBatch(out var file);

        Assert.True(batch.MarkFileReady(file).IsSuccess);
        Assert.Equal("Import.InvalidTransition", batch.MarkFileReady(file).Error.Code);
        Assert.Equal("Import.InvalidTransition", batch.RejectFile(file, "Infected").Error.Code);
        Assert.Equal("Import.DocumentNotFound", batch.MarkFileReady(Guid.NewGuid()).Error.Code);
        Assert.Equal(ImportDocumentStatus.Ready, batch.Documents[0].Status);
    }

    [Fact]
    public void Rejection_keeps_the_reason_and_needs_one()
    {
        var batch = NewBatch(out var file);

        Assert.Equal("Import.FailureReasonRequired", batch.RejectFile(file, " ").Error.Code);
        Assert.True(batch.RejectFile(file, "Infected").IsSuccess);
        Assert.Equal(ImportDocumentStatus.Rejected, batch.Documents[0].Status);
        Assert.Equal("Infected", batch.Documents[0].FailureReason);
    }

    [Fact]
    public void Processing_outcomes_only_apply_to_a_claimed_document()
    {
        var batch = NewBatch(out var file);
        var document = batch.Documents[0].Id;

        Assert.Equal("Import.InvalidTransition", batch.CompleteDocument(document, needsReview: false).Error.Code);
        Assert.Equal("Import.InvalidTransition", batch.StartProcessing(document).Error.Code);
        batch.MarkFileReady(file);
        Assert.True(batch.StartProcessing(document).IsSuccess);
        Assert.True(batch.CompleteDocument(document, needsReview: true).IsSuccess);
        Assert.Equal(ImportDocumentStatus.NeedsReview, batch.Documents[0].Status);
        Assert.Equal("Import.InvalidTransition", batch.FailDocument(document, "Late").Error.Code);
    }

    [Fact]
    public void A_claimed_document_can_return_to_the_queue_hold_or_fail()
    {
        var batch = ReadyBatch(out var document);
        batch.StartProcessing(document);
        Assert.True(batch.ReturnDocumentToQueue(document).IsSuccess);
        Assert.Equal(ImportDocumentStatus.Ready, batch.Documents[0].Status);

        batch.StartProcessing(document);
        Assert.True(batch.HoldDocumentForReview(document, "TenantAiLimit").IsSuccess);
        Assert.Equal(ImportDocumentStatus.NeedsReview, batch.Documents[0].Status);
        Assert.Equal("TenantAiLimit", batch.Documents[0].FailureReason);
    }

    [Fact]
    public void Retry_is_only_allowed_for_failed_documents_without_an_inference_attempt()
    {
        var batch = ReadyBatch(out var document);

        Assert.Equal("Import.NotRetryable", batch.RetryDocument(document, inferenceAttempted: false).Error.Code);
        batch.StartProcessing(document);
        batch.FailDocument(document, "StorageDownload");
        Assert.Equal(
            "Import.InferenceAlreadyAttempted",
            batch.RetryDocument(document, inferenceAttempted: true).Error.Code
        );
        Assert.True(batch.RetryDocument(document, inferenceAttempted: false).IsSuccess);
        Assert.Equal(ImportDocumentStatus.Ready, batch.Documents[0].Status);
        Assert.Null(batch.Documents[0].FailureReason);
    }

    private static ImportBatch NewBatch(out Guid file)
    {
        file = Guid.NewGuid();
        return ImportBatch.Create(Tenant, Customer, User, [file]).Value;
    }

    private static ImportBatch ReadyBatch(out Guid document)
    {
        var batch = NewBatch(out var file);
        batch.MarkFileReady(file);
        document = batch.Documents[0].Id;
        return batch;
    }
}
