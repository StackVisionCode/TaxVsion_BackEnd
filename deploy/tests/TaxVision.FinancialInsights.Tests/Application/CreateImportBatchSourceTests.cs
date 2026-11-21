using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using TaxVision.FinancialInsights.Application.Imports;
using TaxVision.FinancialInsights.Application.Imports.Commands;
using TaxVision.FinancialInsights.Tests.TestDoubles;

namespace TaxVision.FinancialInsights.Tests.Application;

/// <summary>El lote del staff declara en CloudStorage cada estado de cuenta que adopta, con el cliente del lote.</summary>
public sealed class CreateImportBatchSourceTests
{
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _customer = Guid.NewGuid();
    private readonly Guid _user = Guid.NewGuid();

    [Fact]
    public async Task Creating_a_batch_declares_each_document_with_the_customer()
    {
        await using var h = await ReadyHarness();
        var files = new[] { Guid.NewGuid(), Guid.NewGuid() };

        var result = await Create(h, files);

        Assert.True(result.IsSuccess);
        var declared = h.Published.Published.OfType<FileSourceDeclaredIntegrationEvent>().ToList();
        Assert.Equal(files.Order(), declared.Select(evt => evt.FileId).Order());
        Assert.All(
            declared,
            evt =>
            {
                Assert.Equal(_tenant, evt.TenantId);
                Assert.Equal("financial_insights", evt.SourceService);
                Assert.Equal("financial_import_batch", evt.SourceResourceType);
                Assert.Equal(result.Value.Id, evt.SourceResourceId);
                Assert.Equal("statement", evt.SourceRole);
                Assert.Equal(_customer, evt.RelatedCustomerId);
            }
        );
    }

    [Fact]
    public async Task A_batch_that_is_not_stored_declares_nothing()
    {
        await using var h = await ReadyHarness();
        var file = Guid.NewGuid();
        Assert.True((await Create(h, [file])).IsSuccess);
        h.Published.Published.Clear();

        var again = await Create(h, [file]);

        Assert.Equal("Import.FileAlreadyRegistered", again.Error.Code);
        Assert.Empty(h.Published.Published);
    }

    private async Task<FinancialTestHarness> ReadyHarness()
    {
        var h = new FinancialTestHarness(_tenant);
        await h.SeedTenantAsync();
        await h.SeedCustomerAsync(_customer);
        return h;
    }

    private Task<BuildingBlocks.Results.Result<ImportBatchView>> Create(FinancialTestHarness h, Guid[] files) =>
        CreateImportBatchHandler.Handle(
            new CreateImportBatchCommand(_tenant, _user, _customer, files, CanViewAllCustomers: true),
            h.Batches,
            h.Access,
            h.Entitlements,
            h.Files,
            h.Visibility,
            h.Bus,
            CancellationToken.None
        );
}
