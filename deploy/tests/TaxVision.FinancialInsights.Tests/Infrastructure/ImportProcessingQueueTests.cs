using TaxVision.FinancialInsights.Domain.Imports;
using TaxVision.FinancialInsights.Domain.Projections;
using TaxVision.FinancialInsights.Infrastructure.Processing;
using TaxVision.FinancialInsights.Tests.TestDoubles;

namespace TaxVision.FinancialInsights.Tests.Infrastructure;

public sealed class ImportProcessingQueueTests
{
    [Fact]
    public async Task Ready_documents_wait_while_the_tenant_has_no_entitlements_row()
    {
        var tenant = Guid.NewGuid();
        await using var h = new FinancialTestHarness(tenant);
        var batch = ImportBatch.Create(tenant, Guid.NewGuid(), Guid.NewGuid(), [Guid.NewGuid()]).Value;
        batch.MarkDocumentReady(batch.Documents[0].Id);
        await h.Batches.TryAddAsync(batch, CancellationToken.None);
        var queue = new ImportProcessingQueue(h.Db);

        Assert.Empty(await queue.ListClaimCandidatesAsync(10, CancellationToken.None));

        await h.SeedTenantAsync();
        Assert.Equal(batch.Documents[0].Id, Assert.Single(await queue.ListClaimCandidatesAsync(10, CancellationToken.None)).DocumentId);
    }

    [Fact]
    public void Access_rule_holds_only_for_real_changes_and_waits_for_a_missing_row()
    {
        var tenant = Guid.NewGuid();
        var customer = CustomerDirectoryProjection.Create(tenant, Guid.NewGuid(), "c", DateTime.UtcNow);
        var enabled = TenantEntitlementsProjection.Create(tenant, "p", 1, ["financial_insights"], 5);
        var disabled = TenantEntitlementsProjection.Create(tenant, "p", 1, [], 5);

        Assert.Equal(DocumentAccess.Allowed, DocumentProcessingAccess.Evaluate(null, customer, enabled));
        Assert.Equal(DocumentAccess.Pending, DocumentProcessingAccess.Evaluate(null, customer, null));
        Assert.Equal(DocumentAccess.Revoked, DocumentProcessingAccess.Evaluate(null, customer, disabled));
        Assert.Equal(DocumentAccess.Revoked, DocumentProcessingAccess.Evaluate(null, null, enabled));
        var inactive = TenantDirectoryProjection.Create(tenant, "t", DateTime.UtcNow);
        inactive.ApplyStatus("Suspended", false, 2, DateTime.UtcNow);
        Assert.Equal(DocumentAccess.Revoked, DocumentProcessingAccess.Evaluate(inactive, customer, enabled));
    }
}
