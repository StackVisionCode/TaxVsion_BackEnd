using TaxVision.FinancialInsights.Application.Imports;
using TaxVision.FinancialInsights.Application.Imports.Commands;
using TaxVision.FinancialInsights.Application.Imports.Queries;
using TaxVision.FinancialInsights.Domain.Imports;
using TaxVision.FinancialInsights.Tests.TestDoubles;

namespace TaxVision.FinancialInsights.Tests.Application;

public sealed class StaffImportBatchQueryTests
{
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _assigned = Guid.NewGuid();
    private readonly Guid _other = Guid.NewGuid();
    private readonly Guid _user = Guid.NewGuid();

    [Fact]
    public async Task List_is_newest_first_with_names_counts_and_creator()
    {
        await using var h = await Seeded();
        var older = await Batch(h, _assigned, 2);
        await Task.Delay(30);
        var newer = await Batch(h, _other, 1);
        var document = older.Documents[0].Id;
        older.StartProcessing(document);
        older.FailDocument(document, "StorageDownload");
        await h.Db.SaveChangesAsync();

        var page = await List(h, canViewAll: true);

        Assert.Equal([newer.Id, older.Id], page.Items.Select(i => i.Id).ToArray());
        var summary = page.Items[1];
        Assert.Equal("Assigned client", summary.CustomerDisplayName);
        Assert.Equal(_user, summary.CreatedBy);
        Assert.Equal(new ImportDocumentCountsView(2, 0, 1, 0, 0, 0, 0, 1), summary.DocumentCounts);
    }

    [Fact]
    public async Task List_filters_by_customer_status_and_pages()
    {
        await using var h = await Seeded();
        var failed = await Batch(h, _assigned, 1);
        failed.StartProcessing(failed.Documents[0].Id);
        failed.FailDocument(failed.Documents[0].Id, "x");
        await h.Db.SaveChangesAsync();
        await Batch(h, _assigned, 1);
        await Batch(h, _other, 1);

        Assert.Equal(2, (await List(h, true, customerId: _assigned)).TotalCount);
        Assert.Equal(failed.Id, Assert.Single((await List(h, true, status: ImportDocumentStatus.Failed)).Items).Id);
        var paged = await List(h, true, page: 2, pageSize: 2);
        Assert.Single(paged.Items);
        Assert.Equal(3, paged.TotalCount);
    }

    [Fact]
    public async Task Without_view_all_only_assigned_customers_are_listed_or_readable()
    {
        await using var h = await Seeded();
        await h.AssignAsync(_assigned, _user);
        var visible = await Batch(h, _assigned, 1);
        var hidden = await Batch(h, _other, 1);

        var page = await List(h, canViewAll: false);
        Assert.Equal(visible.Id, Assert.Single(page.Items).Id);

        Assert.True((await Get(h, visible.Id, false)).IsSuccess);
        Assert.Equal("Import.BatchNotFound", (await Get(h, hidden.Id, false)).Error.Code);
        Assert.True((await Get(h, hidden.Id, true)).IsSuccess);
    }

    [Fact]
    public async Task Detail_returns_documents_and_movement_suggestions()
    {
        await using var h = await Seeded();
        var batch = await Batch(h, _assigned, 1);
        var document = batch.Documents[0].Id;
        h.Db.FinancialMovements.Add(
            FinancialMovement
                .Create(_tenant, _assigned, document, new DateOnly(2026, 1, 2), "Deposit", 100, "USD", "credit", "Income", 1)
                .Value
        );
        await h.Db.SaveChangesAsync();

        var detail = (await Get(h, batch.Id, true)).Value;

        Assert.Equal("Assigned client", detail.CustomerDisplayName);
        Assert.Equal(document, Assert.Single(detail.Documents).Id);
        Assert.Equal("credit", Assert.Single(detail.Movements).Direction);
    }

    [Fact]
    public async Task Retry_respects_visibility_and_inference_attempts()
    {
        await using var h = await Seeded();
        var batch = await Batch(h, _assigned, 2);
        var failedOnce = batch.Documents[0].Id;
        var billed = batch.Documents[1].Id;
        foreach (var id in new[] { failedOnce, billed })
        {
            batch.StartProcessing(id);
            batch.FailDocument(id, "ProcessingError");
        }
        h.Db.AiProcessingReservations.Add(AiProcessingReservation.Create(_tenant, billed, "m", DateTime.UtcNow).Value);
        await h.Db.SaveChangesAsync();

        Assert.Equal("Import.BatchNotFound", (await Retry(h, batch.Id, failedOnce, false)).Error.Code);
        Assert.Equal("Import.InferenceAlreadyAttempted", (await Retry(h, batch.Id, billed, true)).Error.Code);
        Assert.Equal("Import.DocumentNotFound", (await Retry(h, batch.Id, Guid.NewGuid(), true)).Error.Code);
        var retried = await Retry(h, batch.Id, failedOnce, true);
        Assert.Equal(ImportDocumentStatus.Ready, retried.Value.Status);
        Assert.Equal("Import.NotRetryable", (await Retry(h, batch.Id, failedOnce, true)).Error.Code);
    }

    private async Task<FinancialTestHarness> Seeded()
    {
        var h = new FinancialTestHarness(_tenant);
        await h.SeedTenantAsync();
        await h.SeedCustomerAsync(_assigned, name: "Assigned client");
        await h.SeedCustomerAsync(_other, name: "Other client");
        return h;
    }

    private async Task<ImportBatch> Batch(FinancialTestHarness h, Guid customer, int files)
    {
        var batch = ImportBatch.Create(_tenant, customer, _user, Enumerable.Range(0, files).Select(_ => Guid.NewGuid()).ToArray()).Value;
        foreach (var document in batch.Documents)
            batch.MarkDocumentReady(document.Id);
        Assert.True(await h.Batches.TryAddAsync(batch, CancellationToken.None));
        return batch;
    }

    private async Task<BuildingBlocks.Common.PagedResult<ImportBatchSummaryView>> List(
        FinancialTestHarness h,
        bool canViewAll,
        Guid? customerId = null,
        ImportDocumentStatus? status = null,
        int page = 1,
        int pageSize = 20
    ) =>
        await ListImportBatchesHandler.Handle(
            new ListImportBatchesQuery(_tenant, _user, canViewAll, customerId, status, page, pageSize),
            h.Batches,
            h.Visibility,
            CancellationToken.None
        );

    private Task<BuildingBlocks.Results.Result<ImportBatchDetailView>> Get(FinancialTestHarness h, Guid id, bool canViewAll) =>
        GetImportBatchHandler.Handle(
            new GetImportBatchQuery(_tenant, _user, id, canViewAll),
            h.Batches,
            h.Movements,
            h.Access,
            h.Visibility,
            CancellationToken.None
        );

    private Task<BuildingBlocks.Results.Result<ImportDocumentView>> Retry(
        FinancialTestHarness h,
        Guid batchId,
        Guid documentId,
        bool canViewAll
    ) =>
        RetryImportDocumentHandler.Handle(
            new RetryImportDocumentCommand(_tenant, _user, batchId, documentId, canViewAll),
            h.Batches,
            h.Reservations,
            h.Access,
            h.Db,
            h.Visibility,
            CancellationToken.None
        );
}
