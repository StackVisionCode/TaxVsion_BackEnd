using BuildingBlocks.Messaging.CloudStorageIntegrationEvents;
using TaxVision.FinancialInsights.Application.Imports.Consumers;
using TaxVision.FinancialInsights.Domain.Imports;
using TaxVision.FinancialInsights.Tests.TestDoubles;

namespace TaxVision.FinancialInsights.Tests.Application;

/// <summary>
/// CloudStorage pregunta de quién son archivos sin origen: FinancialInsights declara solo los documentos de sus
/// lotes, como estado de cuenta del lote (igual que el backfill manual), e ignora el resto.
/// </summary>
public sealed class FileSourcesClaimRequestedConsumerTests
{
    private readonly Guid _tenant = Guid.NewGuid();
    private readonly Guid _otherTenant = Guid.NewGuid();
    private readonly Guid _customer = Guid.NewGuid();
    private readonly Guid _otherCustomer = Guid.NewGuid();

    [Fact]
    public async Task Declares_only_the_claimed_files_as_statements_of_their_batch()
    {
        await using var h = new FinancialTestHarness(_tenant);
        var first = await SeedBatchAsync(h, _tenant, _customer, 2);
        var second = await SeedBatchAsync(h, _tenant, _otherCustomer, 1);
        var claimedOfFirst = first.Documents[0].FileId;
        var claimedOfSecond = second.Documents[0].FileId;

        await HandleAsync(h, Claim(_tenant, claimedOfFirst, claimedOfSecond, Guid.NewGuid()));

        var declared = Declared(h);
        Assert.Equal(new[] { claimedOfFirst, claimedOfSecond }.Order(), declared.Select(evt => evt.FileId).Order());
        Assert.All(
            declared,
            evt =>
            {
                Assert.Equal(_tenant, evt.TenantId);
                Assert.Equal("financial_insights", evt.SourceService);
                Assert.Equal("financial_import_batch", evt.SourceResourceType);
                Assert.Equal("statement", evt.SourceRole);
            }
        );
        Assert.Contains(
            declared,
            evt =>
                evt.FileId == claimedOfFirst && evt.SourceResourceId == first.Id && evt.RelatedCustomerId == _customer
        );
        Assert.Contains(
            declared,
            evt =>
                evt.FileId == claimedOfSecond
                && evt.SourceResourceId == second.Id
                && evt.RelatedCustomerId == _otherCustomer
        );
    }

    [Fact]
    public async Task Ids_that_are_not_its_own_publish_nothing()
    {
        await using var h = new FinancialTestHarness(_tenant);
        await SeedBatchAsync(h, _tenant, _customer, 1);

        await HandleAsync(h, Claim(_tenant, Guid.NewGuid(), Guid.NewGuid()));

        Assert.Empty(Declared(h));
    }

    [Fact]
    public async Task Empty_list_or_empty_tenant_does_nothing()
    {
        await using var h = new FinancialTestHarness(_tenant);
        var batch = await SeedBatchAsync(h, _tenant, _customer, 1);

        await HandleAsync(h, Claim(_tenant));
        await HandleAsync(h, Claim(Guid.Empty, batch.Documents[0].FileId));

        Assert.Empty(Declared(h));
    }

    [Fact]
    public async Task Only_looks_in_the_tenant_of_the_claim()
    {
        await using var h = new FinancialTestHarness(_tenant);
        var foreign = await SeedBatchAsync(h, _otherTenant, _customer, 1);
        var fileId = foreign.Documents[0].FileId;

        await HandleAsync(h, Claim(_tenant, fileId));
        Assert.Empty(Declared(h));

        await HandleAsync(h, Claim(_otherTenant, fileId));
        var declared = Assert.Single(Declared(h));
        Assert.Equal(_otherTenant, declared.TenantId);
        Assert.Equal(foreign.Id, declared.SourceResourceId);
    }

    private static List<FileSourceDeclaredIntegrationEvent> Declared(FinancialTestHarness h) =>
        h.Published.Published.OfType<FileSourceDeclaredIntegrationEvent>().ToList();

    private static FileSourcesClaimRequestedIntegrationEvent Claim(Guid tenantId, params Guid[] fileIds) =>
        new() { TenantId = tenantId, FileIds = fileIds };

    private static Task HandleAsync(FinancialTestHarness h, FileSourcesClaimRequestedIntegrationEvent claim) =>
        FileSourcesClaimRequestedConsumer.Handle(claim, h.Batches, h.Bus, h.Correlation, CancellationToken.None);

    private static async Task<ImportBatch> SeedBatchAsync(
        FinancialTestHarness h,
        Guid tenantId,
        Guid customerId,
        int files
    )
    {
        var fileIds = Enumerable.Range(0, files).Select(_ => Guid.NewGuid()).ToList();
        var batch = ImportBatch.Create(tenantId, customerId, Guid.NewGuid(), fileIds).Value;
        Assert.True(await h.Batches.TryAddAsync(batch));
        return batch;
    }
}
