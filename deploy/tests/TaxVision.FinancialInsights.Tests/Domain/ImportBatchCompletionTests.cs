using TaxVision.FinancialInsights.Domain.Imports;
using TaxVision.FinancialInsights.Domain.Imports.Events;

namespace TaxVision.FinancialInsights.Tests.Domain;

public sealed class ImportBatchCompletionTests
{
    private static readonly Guid Tenant = Guid.NewGuid();
    private static readonly Guid Customer = Guid.NewGuid();
    private static readonly Guid User = Guid.NewGuid();

    [Fact]
    public void The_last_document_reaching_a_final_state_raises_one_completion_with_counts()
    {
        var files = new[] { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        var batch = ImportBatch.Create(Tenant, Customer, User, files, ImportBatchSource.Portal).Value;
        var ids = batch.Documents.Select(d => d.Id).ToArray();

        batch.RejectFile(files[3], "Infected");
        foreach (var id in ids[..3])
        {
            batch.MarkDocumentReady(id);
            batch.StartProcessing(id);
        }
        batch.CompleteDocument(ids[0], needsReview: false);
        batch.CompleteDocument(ids[1], needsReview: true);
        Assert.Empty(batch.DomainEvents);
        Assert.Null(batch.CompletionNotifiedAtUtc);

        batch.FailDocument(ids[2], "ProcessingError");

        var completed = Assert.IsType<ImportBatchCompletedDomainEvent>(Assert.Single(batch.DomainEvents));
        Assert.Equal(batch.Id, completed.BatchId);
        Assert.Equal(Tenant, completed.TenantId);
        Assert.Equal(Customer, completed.CustomerId);
        Assert.Equal(User, completed.CreatedBy);
        Assert.Equal(ImportBatchSource.Portal, completed.Source);
        Assert.Equal((1, 1, 1, 1), (completed.Completed, completed.NeedsReview, completed.Failed, completed.Rejected));
        Assert.Equal(batch.CompletionNotifiedAtUtc, completed.CompletedAtUtc);
    }

    [Fact]
    public void Repeated_or_failed_transitions_never_raise_a_second_completion_in_the_same_cycle()
    {
        var file = Guid.NewGuid();
        var batch = ImportBatch.Create(Tenant, Customer, User, [file]).Value;
        Assert.Equal(ImportBatchSource.Staff, batch.Source);

        batch.RejectFile(file, "Infected");
        batch.RejectFile(file, "Infected");
        batch.MarkFileReady(file);

        Assert.Single(batch.DomainEvents);
    }

    [Fact]
    public void A_manual_retry_opens_a_new_cycle_that_completes_once_more()
    {
        var file = Guid.NewGuid();
        var batch = ImportBatch.Create(Tenant, Customer, User, [file]).Value;
        var id = batch.Documents[0].Id;
        batch.MarkDocumentReady(id);
        batch.StartProcessing(id);
        batch.FailDocument(id, "StorageDownload");
        Assert.NotNull(batch.CompletionNotifiedAtUtc);

        batch.RetryDocument(id, inferenceAttempted: false);
        Assert.Null(batch.CompletionNotifiedAtUtc);
        batch.StartProcessing(id);
        batch.CompleteDocument(id, needsReview: false);

        Assert.Equal(2, batch.DomainEvents.Count);
        var second = Assert.IsType<ImportBatchCompletedDomainEvent>(batch.DomainEvents[1]);
        Assert.Equal((1, 0, 0, 0), (second.Completed, second.NeedsReview, second.Failed, second.Rejected));
    }

    [Fact]
    public void Returning_a_document_to_the_queue_does_not_complete_the_batch()
    {
        var batch = ImportBatch.Create(Tenant, Customer, User, [Guid.NewGuid()]).Value;
        var id = batch.Documents[0].Id;
        batch.MarkDocumentReady(id);
        batch.StartProcessing(id);
        batch.ReturnDocumentToQueue(id);

        Assert.Empty(batch.DomainEvents);
    }
}
