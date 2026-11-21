using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using TaxVision.FinancialInsights.Application.Imports;
using TaxVision.FinancialInsights.Application.Imports.Abstractions;
using TaxVision.FinancialInsights.Application.Imports.Commands;
using TaxVision.FinancialInsights.Tests.TestDoubles;

namespace TaxVision.FinancialInsights.Tests.Application;

/// <summary>El lote del portal también declara cada archivo, aunque alguno siga en escaneo.</summary>
public sealed class CreatePortalImportBatchSourceTests
{
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _customer = Guid.NewGuid();

    [Fact]
    public async Task Creating_a_portal_batch_declares_each_document_with_the_customer()
    {
        await using var h = await ReadyHarness();
        var ready = Guid.NewGuid();
        var waiting = Guid.NewGuid();
        h.Files.Set(waiting, ImportFileState.AwaitingScan);

        var result = await Create(h, true, ready, waiting);

        Assert.True(result.IsSuccess);
        var declared = h.Published.Published.OfType<FileSourceDeclaredIntegrationEvent>().ToList();
        Assert.Equal(new[] { ready, waiting }.Order(), declared.Select(evt => evt.FileId).Order());
        Assert.All(
            declared,
            evt =>
            {
                Assert.Equal("financial_import_batch", evt.SourceResourceType);
                Assert.Equal(result.Value.Id, evt.SourceResourceId);
                Assert.Equal("statement", evt.SourceRole);
                Assert.Equal(_customer, evt.RelatedCustomerId);
            }
        );
    }

    [Fact]
    public async Task A_refused_portal_batch_declares_nothing()
    {
        await using var h = await ReadyHarness();

        var refused = await Create(h, false, Guid.NewGuid());

        Assert.True(refused.IsFailure);
        Assert.Empty(h.Published.Published);
    }

    private async Task<FinancialTestHarness> ReadyHarness()
    {
        var h = new FinancialTestHarness(_tenant);
        await h.SeedTenantAsync();
        await h.SeedCustomerAsync(_customer);
        return h;
    }

    private Task<BuildingBlocks.Results.Result<ImportBatchView>> Create(
        FinancialTestHarness h,
        bool canUpload,
        params Guid[] files
    ) =>
        CreatePortalImportBatchHandler.Handle(
            new CreatePortalImportBatchCommand(_tenant, Guid.NewGuid(), _customer, files, canUpload),
            h.Batches,
            h.Access,
            h.Entitlements,
            h.Files,
            h.Db,
            h.Bus,
            CancellationToken.None
        );
}
