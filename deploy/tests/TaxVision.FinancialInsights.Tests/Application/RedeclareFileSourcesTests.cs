using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using BuildingBlocks.Results;
using TaxVision.FinancialInsights.Application.Imports.Abstractions;
using TaxVision.FinancialInsights.Application.Imports.Commands;
using TaxVision.FinancialInsights.Domain.Imports;
using TaxVision.FinancialInsights.Tests.TestDoubles;

namespace TaxVision.FinancialInsights.Tests.Application;

/// <summary>
/// Backfill: re-declara el archivo de cada documento de los lotes del tenant como estado de cuenta del lote.
/// <c>dryRun</c> solo cuenta; repetirlo publica lo mismo.
/// </summary>
public sealed class RedeclareFileSourcesTests
{
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _customer = Guid.NewGuid();
    private readonly Guid _otherCustomer = Guid.NewGuid();

    [Fact]
    public async Task Dry_run_counts_without_publishing()
    {
        await using var h = new FinancialTestHarness(_tenant);
        await SeedBatchAsync(h, _customer, 2);
        await SeedBatchAsync(h, _otherCustomer, 1);
        var publisher = new RecordingPublisher();

        var report = await RunAsync(h, publisher, dryRun: true);

        Assert.True(report.DryRun);
        Assert.Equal(2, report.BatchesScanned);
        Assert.Equal(3, report.FilesDeclared);
        Assert.Empty(publisher.Pages);
    }

    [Fact]
    public async Task Each_document_is_declared_as_a_statement_of_its_batch_and_customer()
    {
        await using var h = new FinancialTestHarness(_tenant);
        var first = await SeedBatchAsync(h, _customer, 2);
        var second = await SeedBatchAsync(h, _otherCustomer, 1);
        var publisher = new RecordingPublisher();

        var report = await RunAsync(h, publisher, dryRun: false);

        var declared = publisher.Pages.SelectMany(page => page).ToList();
        Assert.Equal(3, report.FilesDeclared);
        Assert.Equal(3, declared.Count);
        foreach (var batch in new[] { first, second })
        foreach (var document in batch.Documents)
            Assert.Contains(
                declared,
                evt =>
                    evt.FileId == document.FileId
                    && evt.SourceResourceId == batch.Id
                    && evt.RelatedCustomerId == batch.CustomerId
                    && evt.TenantId == _tenant
                    && evt.SourceService == "financial_insights"
                    && evt.SourceResourceType == "financial_import_batch"
                    && evt.SourceRole == "statement"
            );
    }

    [Fact]
    public async Task Running_it_again_publishes_the_same_declarations()
    {
        await using var h = new FinancialTestHarness(_tenant);
        await SeedBatchAsync(h, _customer, 2);
        var first = new RecordingPublisher();
        var second = new RecordingPublisher();

        await RunAsync(h, first, dryRun: false);
        await RunAsync(h, second, dryRun: false);

        Assert.Equal(
            first.Pages.SelectMany(page => page).Select(evt => evt.FileId).Order(),
            second.Pages.SelectMany(page => page).Select(evt => evt.FileId).Order()
        );
    }

    private async Task<ImportBatch> SeedBatchAsync(FinancialTestHarness h, Guid customerId, int files)
    {
        var fileIds = Enumerable.Range(0, files).Select(_ => Guid.NewGuid()).ToList();
        var batch = ImportBatch.Create(_tenant, customerId, Guid.NewGuid(), fileIds).Value;
        Assert.True(await h.Batches.TryAddAsync(batch));
        return batch;
    }

    private async Task<RedeclaredFileSourcesReport> RunAsync(
        FinancialTestHarness h,
        RecordingPublisher publisher,
        bool dryRun
    )
    {
        var result = await RedeclareFileSourcesHandler.Handle(
            new RedeclareFileSourcesCommand(_tenant, dryRun),
            h.Batches,
            publisher,
            h.Correlation,
            CancellationToken.None
        );
        Assert.True(result.IsSuccess);
        return result.Value;
    }

    private sealed class RecordingPublisher : IFileSourceDeclarationPublisher
    {
        public List<IReadOnlyList<FileSourceDeclaredIntegrationEvent>> Pages { get; } = [];

        public Task<Result> PublishAsync(
            IReadOnlyList<FileSourceDeclaredIntegrationEvent> page,
            CancellationToken ct = default
        )
        {
            Pages.Add(page);
            return Task.FromResult(Result.Success());
        }
    }
}
